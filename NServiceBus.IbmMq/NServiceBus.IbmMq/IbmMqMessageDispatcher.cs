using IBM.WMQ;
using NServiceBus.Transport;

namespace NServiceBus.IbmMq;

internal class IbmMqMessageDispatcher : IMessageDispatcher
{
    private readonly MQQueueManager _queueManager;
    private readonly IbmMqHelper _ibmMqHelper;

    public IbmMqMessageDispatcher(MQQueueManager queueManagerInstance)
    {
        _queueManager = queueManagerInstance;
        _ibmMqHelper = new IbmMqHelper(_queueManager);
    }

    public async Task Dispatch(TransportOperations outgoingMessages, TransportTransaction transaction, CancellationToken cancellationToken = default)
    {
        foreach (var transportOperation in outgoingMessages.UnicastTransportOperations)
        {
            await DispatchUnicast(transportOperation).ConfigureAwait(false);
        }

        foreach (var transportOperation in outgoingMessages.MulticastTransportOperations)
        {
            await DispatchMulticast(transportOperation).ConfigureAwait(false);
        }
    }

    Task DispatchUnicast(UnicastTransportOperation unicastTransportOperation)
    {
        using var queue = _ibmMqHelper.EnsureQueue(unicastTransportOperation.Destination, MQC.MQOO_OUTPUT);

        MQMessage message = _ibmMqHelper.CreateMessage(unicastTransportOperation.Message);

        MQPutMessageOptions putOptions = new();

        queue.Put(message, putOptions);

        return Task.CompletedTask;
    }

    private Task DispatchMulticast(MulticastTransportOperation transportOperation)
    {
        using var topic = _ibmMqHelper.EnsureTopic(transportOperation.MessageType);

        var message = _ibmMqHelper.CreateMessage(transportOperation.Message);

        topic.Put(message);

        return Task.CompletedTask;
    }
}
