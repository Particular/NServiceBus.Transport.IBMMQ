namespace NServiceBus.Transport.IbmMq;

using IBM.WMQ;

class MessageDispatcher(MqQueueManagerFacade sendFacade, TopicTopology topology) : IMessageDispatcher
{
    protected readonly record struct DispatchContext(MqQueueManagerFacade Facade, int PutOptions);

    public virtual Task Dispatch(TransportOperations outgoingMessages, TransportTransaction transaction, CancellationToken cancellationToken = default)
    {
        var context = ResolveContext(transaction);

        Dictionary<string, MQQueue>? queues = null;
        Dictionary<string, MQTopic>? topics = null;

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

    protected static void DispatchUnicast(UnicastTransportOperation operation, Dictionary<string, MQQueue> queues, DispatchContext context)
    {
        if (!queues.TryGetValue(operation.Destination, out var queue))
        {
            queue = context.Facade.AccessSendQueue(operation.Destination);
            queues[operation.Destination] = queue;
        }

        var message = IbmMqMessageConverter.ToNative(operation);
        queue.Put(message, new MQPutMessageOptions { Options = context.PutOptions });
    }

    protected void DispatchMulticast(MulticastTransportOperation operation, Dictionary<string, MQTopic> topics, DispatchContext context)
    {
        var putOptions = new MQPutMessageOptions { Options = context.PutOptions };

        foreach (var destination in topology.GetPublishDestinations(operation.MessageType))
        {
            if (!topics.TryGetValue(destination.TopicName, out var topic))
            {
                topic = context.Facade.EnsureTopic(destination.TopicName, destination.TopicString);
                topics[destination.TopicName] = topic;
            }

            var message = IbmMqMessageConverter.ToNative(operation);
            topic.Put(message, putOptions);
        }
    }
}
