using IBM.WMQ;

namespace NServiceBus.Transport.IbmMq;

class IbmMqTransportInfrastructure : TransportInfrastructure, IDisposable
{
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
        ((IDisposable)receiveQueueManager).Dispose();
        ((IDisposable)sendQueueManager).Dispose();
    }
}