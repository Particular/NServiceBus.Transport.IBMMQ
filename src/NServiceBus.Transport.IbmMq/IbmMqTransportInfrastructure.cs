namespace NServiceBus.Transport.IbmMq;

using IBM.WMQ;
using NServiceBus.Logging;

class IbmMqTransportInfrastructure : TransportInfrastructure, IDisposable
{
    static readonly ILog Log = LogManager.GetLogger<IbmMqTransportInfrastructure>();

    readonly MQConnectionPool connectionPool;
    readonly MQQueueManager sendQueueManager;
    readonly Func<string, string>? queueNameFormatter;
    readonly int messageWaitInterval;
    bool _disposed;

    public IbmMqTransportInfrastructure(
        IbmMqTransportOptions options,
        ConnectionConfiguration connectionConfiguration,
        ReceiveSettings[] receiverSettings
    )
    {
        ArgumentNullException.ThrowIfNull(connectionConfiguration);
        ArgumentNullException.ThrowIfNull(receiverSettings);

        MQQueueManager CreateQueueManager() => new(connectionConfiguration.QueueManagerName, connectionConfiguration.ConnectionProperties);


        Log.InfoFormat("Connecting to IBM MQ Queue Manager: {0}", connectionConfiguration.QueueManagerName);

        connectionPool = new MQConnectionPool(CreateQueueManager);
        sendQueueManager = CreateQueueManager();
        queueNameFormatter = options.QueueNameFormatter;
        messageWaitInterval = connectionConfiguration.MessageWaitInterval;

        Dispatcher = new IbmMqMessageDispatcher(CreateHelper(sendQueueManager));
        Receivers = receiverSettings
            .ToDictionary(
                x => x.Id,
                x =>
                {
                    var subMgr = new IbmMqSubscriptionManager(CreateHelper, connectionPool, x.ReceiveAddress.BaseAddress);
                    return new IbmMqMessageReceiver(CreateWorker, subMgr, x) as IMessageReceiver;
                }
            );
    }

    public override async Task Shutdown(CancellationToken cancellationToken = default)
    {
        Log.Debug("Shutdown");
        var tasks = new List<Task>();
        foreach (var receiver in Receivers.Values)
        {
            tasks.Add(((IAsyncDisposable)receiver).DisposeAsync().AsTask());
        }

        await Task.WhenAll(tasks)
            .ConfigureAwait(false);


        Dispose();
    }

    public override string ToTransportAddress(QueueAddress address)
    {
        return address.BaseAddress;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Log.Debug("Disposing");
        connectionPool.Dispose();

        try
        {
            sendQueueManager.Disconnect();
        }
        catch (MQException ex)
        {
            Log.Warn("Failed to disconnect send queue manager", ex);
        }

        try
        {
            ((IDisposable)sendQueueManager).Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to dispose send queue manager", ex);
        }
    }

    IbmMqHelper CreateHelper(MQQueueManager qm) =>
        new(qm, queueNameFormatter);

    MessagePumpWorker CreateWorker(string queue, OnMessage onMsg, OnError onErr, int idx) =>
        new(messageWaitInterval, connectionPool, queue, onMsg, onErr, idx);
}