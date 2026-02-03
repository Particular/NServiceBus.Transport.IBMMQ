using IBM.WMQ;
using NServiceBus.Transport;

namespace NServiceBus.IbmMq;

class IbmMqTransportInfrastructure : TransportInfrastructure, IDisposable
{
    MQQueueManager receiveQueueManagerInstance = new MQQueueManager("QM1");
    MQQueueManager sendQueueManagerInstance = new MQQueueManager("QM1");

    public IbmMqTransportInfrastructure(ReceiveSettings[] receiverSettings)
    {
        Dispatcher = new IbmMqMessageDispatcher(sendQueueManagerInstance);

        Receivers = receiverSettings.ToDictionary(x => x.Id,
            x => new IbmMqMessageReceiver(receiveQueueManagerInstance, x) as IMessageReceiver);
    }

    public override Task Shutdown(CancellationToken cancellationToken = default)
    {
        receiveQueueManagerInstance?.Disconnect();
        sendQueueManagerInstance?.Disconnect();
        return Task.CompletedTask;
    }

    public override string ToTransportAddress(QueueAddress address)
    {
        return address.BaseAddress;
    }

    public void Dispose()
    {
        ((IDisposable)receiveQueueManagerInstance)?.Dispose();
        ((IDisposable)sendQueueManagerInstance)?.Dispose();
    }
}