namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;

class MessageDispatcher(MqConnectionPool sendPool, TopicTopology topology) : IMessageDispatcher
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

            sendPool.Return(context.Facade);
        }

        return Task.CompletedTask;
    }

    protected virtual DispatchContext ResolveContext(TransportTransaction transaction)
    {
        var facade = sendPool.Rent();
        return new(facade, MQC.MQPMO_FAIL_IF_QUIESCING);
    }

    protected void ReturnToPool(MqQueueManagerFacade facade) => sendPool.Return(facade);

    protected static void DispatchUnicast(UnicastTransportOperation operation, Dictionary<string, MQQueue> queues, DispatchContext context)
    {
        if (!queues.TryGetValue(operation.Destination, out var queue))
        {
            queue = context.Facade.AccessSendQueue(operation.Destination);
            queues[operation.Destination] = queue;
        }

        var message = IBMMQMessageConverter.ToNative(operation);
        queue.Put(message, new MQPutMessageOptions { Options = context.PutOptions });
    }

    protected void DispatchMulticast(MulticastTransportOperation operation, Dictionary<string, MQTopic> topics, DispatchContext context)
    {
        var putOptions = new MQPutMessageOptions { Options = context.PutOptions };

        foreach (var destination in topology.GetPublishDestinations(operation.MessageType))
        {
            if (!topics.TryGetValue(destination.TopicName, out var topic))
            {
                try
                {
                    topic = context.Facade.AccessTopic(destination.TopicString);
                }
                catch (MQException)
                {
                    CreateTopicOrThrow(context.Facade, destination.TopicName, destination.TopicString);
                    topic = context.Facade.AccessTopic(destination.TopicString);
                }

                topics[destination.TopicName] = topic;
            }

            var message = IBMMQMessageConverter.ToNative(operation);
            topic.Put(message, putOptions);
        }
    }

    static void CreateTopicOrThrow(MqQueueManagerFacade facade, string topicName, string topicString)
    {
        try
        {
            facade.CreateTopic(topicName, topicString);
        }
        catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NOT_AUTHORIZED)
        {
            throw new InvalidOperationException(
                $"Topic '{topicName}' does not exist and the current user is not authorized to create it. " +
                "Pre-create topics by running the endpoint with EnableInstallers using an account with administrative permissions, " +
                "or have an MQ administrator create the topic.", ex);
        }
    }
}
