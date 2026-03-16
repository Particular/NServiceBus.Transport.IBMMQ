namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;

class MessageDispatcher(MqConnectionPool sendPool, TopicTopology topology, DestinationCache<MQQueue> queueCache, DestinationCache<MQTopic> topicCache) : IMessageDispatcher
{
    protected readonly record struct DispatchContext(MqQueueManagerFacade Facade, int PutOptions);

    public virtual Task Dispatch(TransportOperations outgoingMessages, TransportTransaction transaction, CancellationToken cancellationToken = default)
    {
        var context = ResolveContext(transaction);
        var putOptions = new MQPutMessageOptions { Options = context.PutOptions };

        try
        {
            foreach (var operation in outgoingMessages.UnicastTransportOperations)
            {
                var queue = queueCache.GetOrAdd(operation.Destination, context.Facade.AccessSendQueue);
                var message = IBMMQMessageConverter.ToNative(operation);

                try
                {
                    queue.Put(message, putOptions);
                }
                catch (MQException)
                {
                    queueCache.Evict(operation.Destination);
                    throw;
                }
            }

            foreach (var operation in outgoingMessages.MulticastTransportOperations)
            {
                foreach (var destination in topology.GetPublishDestinations(operation.MessageType))
                {
                    var topic = topicCache.GetOrAdd(destination.TopicName, _ =>
                        context.Facade.EnsureTopic(destination.TopicName, destination.TopicString));

                    // Message cannot be re-used, is modified by .Put(..)
                    var message = IBMMQMessageConverter.ToNative(operation);

                    try
                    {
                        topic.Put(message, putOptions);
                    }
                    catch (MQException)
                    {
                        topicCache.Evict(destination.TopicName);
                        throw;
                    }
                }
            }
        }
        finally
        {
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
                topic = context.Facade.EnsureTopic(destination.TopicName, destination.TopicString);
                topics[destination.TopicName] = topic;
            }

            // Message cannot be re-used, is modified by .Put(..)
            var message = IBMMQMessageConverter.ToNative(operation);
            topic.Put(message, putOptions);
        }
    }

}
