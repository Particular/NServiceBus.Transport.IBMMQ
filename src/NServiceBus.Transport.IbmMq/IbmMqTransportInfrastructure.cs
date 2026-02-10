namespace NServiceBus.Transport.IbmMq;

using IBM.WMQ;
using NServiceBus.Logging;

class IbmMqTransportInfrastructure : TransportInfrastructure, IAsyncDisposable, IDisposable
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
        await DisposeAsync().ConfigureAwait(false);
    }

    public override string ToTransportAddress(QueueAddress address)
    {
        return address.BaseAddress;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Log.Debug("Disposing");

        var tasks = new List<Task>();
        foreach (var receiver in Receivers.Values)
        {
            if (receiver is IAsyncDisposable disposable)
            {
                tasks.Add(disposable.DisposeAsync().AsTask());
            }
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        DisposeCore();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Log.Debug("Disposing");
        DisposeCore();
    }

    void DisposeCore()
    {
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