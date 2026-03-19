namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
using Logging;

sealed class MqConnection(
    ILog log,
    MQQueueManager queueManager,
    SanitizeResourceName resourceNameFormatter,
    CreateTopic createTopic,
    int cacheCapacity
) : IDisposable, IAsyncDisposable
{
    readonly DestinationCache<MQQueue> queueCache = new(log, cacheCapacity);
    readonly DestinationCache<MQTopic> topicCache = new(log, cacheCapacity);
    int _disposed;

    public void PutToQueue(string destination, MQMessage message, MQPutMessageOptions options)
    {
        var queue = queueCache.GetOrAdd(destination, OpenSendQueue);
        try
        {
            queue.Put(message, options);
        }
        catch (MQException)
        {
            queueCache.Evict(destination);
            throw;
        }
    }

    public void PutToTopic(string topicName, string topicString, MQMessage message, MQPutMessageOptions options)
    {
        var topic = topicCache.GetOrAdd(topicName, _ => EnsureTopic(topicName, topicString));
        try
        {
            topic.Put(message, options);
        }
        catch (MQException)
        {
            topicCache.Evict(topicName);
            throw;
        }
    }

    internal MQQueue GetOrOpenSendQueue(string name) =>
        queueCache.GetOrAdd(name, OpenSendQueue);

    MQQueue OpenSendQueue(string name)
    {
        var formatted = resourceNameFormatter(name);
        return queueManager.AccessQueue(formatted, MQC.MQOO_OUTPUT);
    }

    MQTopic AccessTopic(string topicString) =>
        queueManager.AccessTopic(topicString, null, MQC.MQTOPIC_OPEN_AS_PUBLICATION, MQC.MQOO_OUTPUT);

    MQTopic EnsureTopic(string topicName, string topicString)
    {
        try
        {
            return AccessTopic(topicString);
        }
        catch (MQException)
        {
            // IBM MQ does not return a single distinguishable reason code for
            // "topic object does not exist"; the error depends on queue manager
            // configuration. Optimistically attempt to create the admin object
            // on any failure. CreateTopic is idempotent (ignores "already exists")
            // and translates authorization errors into a descriptive exception.
            createTopic(topicName, topicString);
            return AccessTopic(topicString);
        }
    }

    public MQQueue OpenInputQueue(string name) =>
        queueManager.AccessQueue(name, MQC.MQOO_INPUT_AS_Q_DEF);

    public void Commit() => queueManager.Commit();
    public void Backout() => queueManager.Backout();

    public void Disconnect()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        try
        {
            queueCache.Dispose();
        }
        finally
        {
            try
            {
                topicCache.Dispose();
            }
            finally
            {
                // Disconnect() performs a graceful close of the connection; the subsequent
                // Dispose() is a no-op if already disconnected but ensures cleanup if
                // Disconnect() throws.
                using (queueManager)
                {
                    queueManager.Disconnect();
                }
            }
        }
    }

    public void Dispose() => Disconnect();

    public ValueTask DisposeAsync()
    {
        Disconnect();
        return ValueTask.CompletedTask;
    }
}
