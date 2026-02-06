using IBM.WMQ;
using NServiceBus.Logging;
using NServiceBus.Transport.IbmMq.Configuration;

namespace NServiceBus.Transport.IbmMq;

class IbmMqTransportInfrastructure : TransportInfrastructure, IDisposable
{
    static readonly ILog Log = LogManager.GetLogger<IbmMqTransportInfrastructure>();

    readonly MQConnectionPool connectionPool;
    readonly MQQueueManager sendQueueManager;

    public IbmMqTransportInfrastructure(ConnectionConfiguration connectionConfiguration, ReceiveSettings[] receiverSettings)
    {
        ArgumentNullException.ThrowIfNull(connectionConfiguration);
        ArgumentNullException.ThrowIfNull(receiverSettings);

        var queueManagerName = connectionConfiguration.QueueManagerName ?? string.Empty;
        var createQueueManager = () => new MQQueueManager(queueManagerName, connectionConfiguration.ConnectionProperties);

        Log.Info($"Connecting to IBM MQ Queue Manager: {(string.IsNullOrWhiteSpace(queueManagerName) ? "(default)" : queueManagerName)}");

        connectionPool = new MQConnectionPool(createQueueManager);
        sendQueueManager = createQueueManager();

        Dispatcher = new IbmMqMessageDispatcher(new IbmMqHelper(sendQueueManager));
        Receivers = receiverSettings
            .ToDictionary(
                x => x.Id,
                x => new IbmMqMessageReceiver(connectionPool, x) as IMessageReceiver
            );
    }

    public override Task Shutdown(CancellationToken cancellationToken = default)
    {
        Log.Debug("Shutting down IbmMqTransportInfrastructure");
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
        Log.Debug("Disposing IbmMqTransportInfrastructure");
        connectionPool.Dispose();
        ((IDisposable)sendQueueManager).Dispose();
    }
}