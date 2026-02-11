namespace NServiceBus.Transport.IbmMq;

using IBM.WMQ;
using Logging;

sealed class IbmMqMessageDispatcher(ILog log, MQQueueManager sendConnection, MqQueueManagerFacade facade) : IMessageDispatcher, IDisposable
{
    public Task Dispatch(TransportOperations outgoingMessages, TransportTransaction transaction, CancellationToken cancellationToken = default)
    {
        Dictionary<string, MQQueue>? queues = null;
        Dictionary<Type, MQTopic>? topics = null;

        try
        {
            if (outgoingMessages.UnicastTransportOperations.Count > 0)
            {
                queues = [];

                foreach (var transportOperation in outgoingMessages.UnicastTransportOperations)
                {
                    DispatchUnicast(transportOperation, queues);
                }
            }

            if (outgoingMessages.MulticastTransportOperations.Count > 0)
            {
                topics = [];

                foreach (var transportOperation in outgoingMessages.MulticastTransportOperations)
                {
                    DispatchMulticast(transportOperation, topics);
                }
            }
        }
        finally
        {
            if (queues != null)
            {
                foreach (var queue in queues.Values)
                {
                    using (queue)
                    {
                        queue.Close();
                    }
                }
            }

            if (topics != null)
            {
                foreach (var topic in topics.Values)
                {
                    using (topic)
                    {
                        topic.Close();
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    void DispatchUnicast(UnicastTransportOperation unicastTransportOperation, Dictionary<string, MQQueue> queues)
    {
        if (!queues.TryGetValue(unicastTransportOperation.Destination, out var queue))
        {
            queue = facade.AccessSendQueue(unicastTransportOperation.Destination);
            queues[unicastTransportOperation.Destination] = queue;
        }

        var message = IbmMqMessageConverter.ToNative(unicastTransportOperation.Message);

        MQPutMessageOptions putOptions = new();

        // TODO: Correct transaction management when NOT receive only but sendsatomicwithreceive
        //putOptions.Options |= MQC.MQPMO_SYNCPOINT | // Include in transaction

        // TODO: Evaluate if MQPMO_NEW_MSG_ID must be set if we already set the MessagID based on the message ID header.
        //putOptions.Options |= MQC.MQPMO_NEW_MSG_ID; // Generate unique MQ message ID

        putOptions.Options |= MQC.MQPMO_FAIL_IF_QUIESCING;

        queue.Put(message, putOptions);
    }

    void DispatchMulticast(MulticastTransportOperation transportOperation, Dictionary<Type, MQTopic> topics)
    {
        if (!topics.TryGetValue(transportOperation.MessageType, out var topic))
        {
            topic = facade.EnsureTopic(transportOperation.MessageType);
            topics[transportOperation.MessageType] = topic;
        }

        var message = IbmMqMessageConverter.ToNative(transportOperation.Message);
        topic.Put(message);
    }

    public void Dispose()
    {
        using (sendConnection)
        {
            try
            {
                sendConnection.Disconnect();
            }
            catch (MQException ex)
            {
                log.Warn("Failed to disconnect send connection", ex);
            }
        }
    }
}
