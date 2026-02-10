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

    public override Task Shutdown(CancellationToken cancellationToken = default)
    {
        Log.Debug("Shutdown");
        connectionPool.Dispose();
        sendQueueManager.Disconnect();
        return Task.CompletedTask;
    }

    public override string ToTransportAddress(QueueAddress address)
    {
        return address.BaseAddress;
    }

    public void Dispose()
    {
        Log.Debug("Disposing");
        connectionPool.Dispose();
        ((IDisposable)sendQueueManager).Dispose();
    }

    IbmMqHelper CreateHelper(MQQueueManager qm) =>
        new(qm, queueNameFormatter);

    MessagePumpWorker CreateWorker(string queue, OnMessage onMsg, OnError onErr, int idx) =>
        new(messageWaitInterval, connectionPool, queue, onMsg, onErr, idx);
}