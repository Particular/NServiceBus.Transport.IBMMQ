namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
using Logging;

sealed class MqConnection(MQQueueManager queueManager, SanitizeResourceName resourceNameFormatter, int cacheCapacity)
    : IDisposable, IAsyncDisposable
{
    static readonly ILog log = LogManager.GetLogger<MqConnection>();
    readonly DestinationCache<MQQueue> queueCache = new(log, cacheCapacity);
    readonly DestinationCache<MQTopic> topicCache = new(log, cacheCapacity);

    public MQQueue GetOrOpenSendQueue(string name) =>
        queueCache.GetOrAdd(name, AccessSendQueue);

    public MQTopic GetOrOpenTopic(string topicName, string topicString) =>
        topicCache.GetOrAdd(topicName, _ => EnsureTopic(topicName, topicString));

    public MQQueue AccessSendQueue(string name)
    {
        var formatted = resourceNameFormatter(name);
        return queueManager.AccessQueue(formatted, MQC.MQOO_OUTPUT);
    }

    public MQTopic AccessTopic(string topicString) =>
        queueManager.AccessTopic(topicString, null, MQC.MQTOPIC_OPEN_AS_PUBLICATION, MQC.MQOO_OUTPUT);

    public MQTopic EnsureTopic(string topicName, string topicString)
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
            // on any failure. CreateTopicOrThrow is idempotent (ignores
            // "already exists") and translates authorization errors into a
            // descriptive exception.
            CreateTopicOrThrow(topicName, topicString);
            return AccessTopic(topicString);
        }
    }

    public void EvictQueue(string name) => queueCache.Evict(name);
    public void EvictTopic(string name) => topicCache.Evict(name);

    public MQQueue OpenInputQueue(string name) =>
        queueManager.AccessQueue(name, MQC.MQOO_INPUT_AS_Q_DEF);

    public void Commit() => queueManager.Commit();
    public void Backout() => queueManager.Backout();

    public void Disconnect()
    {
        queueCache.Dispose();
        topicCache.Dispose();
        using (queueManager)
        {
            queueManager.Disconnect();
        }
    }

    public void Dispose() => Disconnect();

    public ValueTask DisposeAsync()
    {
        Disconnect();
        return ValueTask.CompletedTask;
    }

    void CreateTopicOrThrow(string topicName, string topicString)
    {
        try
        {
            var agent = new IBM.WMQ.PCF.PCFMessageAgent(queueManager);
            try
            {
                var command = new IBM.WMQ.PCF.PCFMessage(MQC.MQCMD_CREATE_TOPIC);
                command.AddParameter(MQC.MQCA_TOPIC_NAME, topicName);
                command.AddParameter(MQC.MQCA_TOPIC_STRING, topicString);
                agent.Send(command);
            }
            catch (IBM.WMQ.PCF.PCFException e) when (e.ReasonCode == MQC.MQRCCF_OBJECT_ALREADY_EXISTS)
            {
            }
            finally
            {
                agent.Disconnect();
            }
        }
        catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NOT_AUTHORIZED)
        {
            throw new InvalidOperationException(
                $"Topic '{topicName}' does not exist and the current user is not authorized to create it. " +
                "Pre-create topics by running the endpoint with EnableInstallers using an account with administrative permissions, " +
                "or have an MQ administrator create the topic.", ex);
        }
    }
}