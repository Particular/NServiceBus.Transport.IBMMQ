using IBM.WMQ;
using NServiceBus.Logging;

namespace NServiceBus.Transport.IbmMq;

class IbmMqTransportInfrastructure : TransportInfrastructure, IDisposable
{
    static readonly ILog Log = LogManager.GetLogger<IbmMqTransportInfrastructure>();

    MQQueueManager receiveQueueManager = new("QM1");
    MQQueueManager sendQueueManager = new("QM1");

    public IbmMqTransportInfrastructure(ReceiveSettings[] receiverSettings)
    {
        Dispatcher = new IbmMqMessageDispatcher(new IbmMqHelper(sendQueueManager));

        Receivers = receiverSettings
            .ToDictionary(
                x => x.Id,
                x => new IbmMqMessageReceiver(receiveQueueManager, x) as IMessageReceiver
            );
    }

    public override Task Shutdown(CancellationToken cancellationToken = default)
    {
        Log.Debug("Shutting down IbmMqTransportInfrastructure");
        receiveQueueManager.Disconnect();
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
        ((IDisposable)receiveQueueManager).Dispose();
        ((IDisposable)sendQueueManager).Dispose();
    }
}