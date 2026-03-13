namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;

class MessageDispatcher(MqConnectionPool sendPool, TopicTopology topology) : IMessageDispatcher
{
    protected readonly record struct DispatchContext(MqQueueManagerFacade Facade, int PutOptions);

    public virtual Task Dispatch(TransportOperations outgoingMessages, TransportTransaction transaction, CancellationToken cancellationToken = default)
    {
        var context = ResolveContext(transaction);

        try
        {
            foreach (var operation in outgoingMessages.UnicastTransportOperations)
            {
                DispatchUnicast(operation, context);
            }

            foreach (var operation in outgoingMessages.MulticastTransportOperations)
            {
                DispatchMulticast(operation, context);
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

    protected static void DispatchUnicast(UnicastTransportOperation operation, DispatchContext context)
    {
        var queue = context.Facade.AccessSendQueue(operation.Destination);
        var message = IBMMQMessageConverter.ToNative(operation);
        queue.Put(message, new MQPutMessageOptions { Options = context.PutOptions });
    }

    protected void DispatchMulticast(MulticastTransportOperation operation, DispatchContext context)
    {
        var putOptions = new MQPutMessageOptions { Options = context.PutOptions };

        foreach (var destination in topology.GetPublishDestinations(operation.MessageType))
        {
            try
            {
                var topic = context.Facade.AccessTopic(destination.TopicString);
                var message = IBMMQMessageConverter.ToNative(operation);
                topic.Put(message, putOptions);
            }
            catch (MQException)
            {
                CreateTopicOrThrow(context.Facade, destination.TopicName, destination.TopicString);
                // Evict failed cache entry and retry
                context.Facade.CloseCachedHandles();
                var topic = context.Facade.AccessTopic(destination.TopicString);
                var message = IBMMQMessageConverter.ToNative(operation);
                topic.Put(message, putOptions);
            }
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
