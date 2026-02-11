namespace NServiceBus.Transport.IbmMq;

using IBM.WMQ;

class MessageDispatcher(MqQueueManagerFacade sendFacade) : IMessageDispatcher
{
    protected readonly record struct DispatchContext(MqQueueManagerFacade Facade, int PutOptions);

    public Task Dispatch(TransportOperations outgoingMessages, TransportTransaction transaction, CancellationToken cancellationToken = default)
    {
        var context = ResolveContext(transaction);

        Dictionary<string, MQQueue>? queues = null;
        Dictionary<Type, MQTopic>? topics = null;

        try
        {
            if (outgoingMessages.UnicastTransportOperations.Count > 0)
            {
                queues = [];

                foreach (var operation in outgoingMessages.UnicastTransportOperations)
                {
                    DispatchUnicast(operation, queues, context);
                }
            }

            if (outgoingMessages.MulticastTransportOperations.Count > 0)
            {
                topics = [];

                foreach (var operation in outgoingMessages.MulticastTransportOperations)
                {
                    DispatchMulticast(operation, topics, context);
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

    protected virtual DispatchContext ResolveContext(TransportTransaction transaction) =>
        new(sendFacade, MQC.MQPMO_FAIL_IF_QUIESCING);

    static void DispatchUnicast(UnicastTransportOperation operation, Dictionary<string, MQQueue> queues, DispatchContext context)
    {
        if (!queues.TryGetValue(operation.Destination, out var queue))
        {
            queue = context.Facade.AccessSendQueue(operation.Destination);
            queues[operation.Destination] = queue;
        }

        var message = IbmMqMessageConverter.ToNative(operation.Message);
        queue.Put(message, new MQPutMessageOptions { Options = context.PutOptions });
    }

    static void DispatchMulticast(MulticastTransportOperation operation, Dictionary<Type, MQTopic> topics, DispatchContext context)
    {
        if (!topics.TryGetValue(operation.MessageType, out var topic))
        {
            topic = context.Facade.EnsureTopic(operation.MessageType);
            topics[operation.MessageType] = topic;
        }

        var message = IbmMqMessageConverter.ToNative(operation.Message);
        topic.Put(message, new MQPutMessageOptions { Options = context.PutOptions });
    }
}
