using IBM.WMQ;
using NServiceBus.Logging;

namespace NServiceBus.Transport.IbmMq;

class IbmMqTransportInfrastructure : TransportInfrastructure, IDisposable
{
    const string QueueManagerName = "QM1"; // TODO: Use settings object from WIP PR
    static readonly ILog Log = LogManager.GetLogger<IbmMqTransportInfrastructure>();

    // TODO: Use settings object from WIP PR
    readonly MQConnectionPool connectionPool = new(QueueManagerName);
    readonly MQQueueManager sendQueueManager = new(QueueManagerName);

    public IbmMqTransportInfrastructure(ReceiveSettings[] receiverSettings)
    {
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