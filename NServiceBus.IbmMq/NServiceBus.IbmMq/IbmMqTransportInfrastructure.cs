using IBM.WMQ;
using NServiceBus.Transport;

namespace NServiceBus.IbmMq;

class IbmMqTransportInfrastructure : TransportInfrastructure
{
    MQQueueManager queueManagerInstance;

    public IbmMqTransportInfrastructure(ReceiveSettings[] receiverSettings)
    {
        queueManagerInstance = new MQQueueManager("QM1");
        Dispatcher = new IbmMqMessageDispatcher(queueManagerInstance);

        Receivers = receiverSettings.ToDictionary(x => x.Id,
            x => new IbmMqMessageReceiver(queueManagerInstance, x) as IMessageReceiver);
    }

    public override Task Shutdown(CancellationToken cancellationToken = default)
    {
        queueManagerInstance?.Disconnect();
        return Task.CompletedTask;
    }

    public override string ToTransportAddress(QueueAddress address)
    {
        return address.BaseAddress;
    }
}
