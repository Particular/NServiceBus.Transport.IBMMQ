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
        // Queue is not thread-safe and cannot be concurrently accessed.
        using var queue = _ibmMqHelper.EnsureQueue(unicastTransportOperation.Destination, MQC.MQOO_OUTPUT);

        MQMessage message = _ibmMqHelper.CreateMessage(unicastTransportOperation.Message);

        MQPutMessageOptions putOptions = new();

        // TODO: Correct transaction management when NOT receive only but sendsatomicwithreceive
        //putOptions.Options |= MQC.MQPMO_SYNCPOINT | // Include in transaction

        // TODO: Evaluate if MQPMO_NEW_MSG_ID must be set if we already set the MessagID based on the message ID header.
        //putOptions.Options |= MQC.MQPMO_NEW_MSG_ID; // Generate unique MQ message ID

        putOptions.Options |=  MQC.MQPMO_FAIL_IF_QUIESCING;

        queue.Put(message, putOptions);

        queue.Close(); // Also done in Dipose, but cleaner as this indicate a normal sequence

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
