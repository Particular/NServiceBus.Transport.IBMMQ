namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;

sealed class AtomicMessageDispatcher(MqConnectionPool pool, TopicTopology topology, CreateQueueManagerFacade createFacade, IBMMQMessageConverter messageConverter, DestinationCache<MQQueue> queueCache, DestinationCache<MQTopic> topicCache)
    : MessageDispatcher(pool, topology, messageConverter, queueCache, topicCache)
{
    public override Task Dispatch(TransportOperations outgoingMessages, TransportTransaction transaction, CancellationToken cancellationToken = default)
    {
        if (!transaction.TryGet<MQQueueManager>(out var receiveConnection))
        {
            return base.Dispatch(outgoingMessages, transaction, cancellationToken);
        }

        var atomicContext = new DispatchContext(createFacade(receiveConnection), MQC.MQPMO_FAIL_IF_QUIESCING | MQC.MQPMO_SYNCPOINT);
        DispatchContext? isolatedContext = null;

        Dictionary<string, MQQueue>? atomicQueues = null;
        Dictionary<string, MQQueue>? isolatedQueues = null;
        Dictionary<string, MQTopic>? atomicTopics = null;
        Dictionary<string, MQTopic>? isolatedTopics = null;

        try
        {
            foreach (var operation in outgoingMessages.UnicastTransportOperations)
            {
                if (operation.RequiredDispatchConsistency == DispatchConsistency.Isolated)
                {
                    isolatedContext ??= ResolveContext(transaction);
                    isolatedQueues ??= [];
                    DispatchUnicast(operation, isolatedQueues, isolatedContext.Value);
                }
                else
                {
                    atomicQueues ??= [];
                    DispatchUnicast(operation, atomicQueues, atomicContext);
                }
            }

            foreach (var operation in outgoingMessages.MulticastTransportOperations)
            {
                if (operation.RequiredDispatchConsistency == DispatchConsistency.Isolated)
                {
                    isolatedContext ??= ResolveContext(transaction);
                    isolatedTopics ??= [];
                    DispatchMulticast(operation, isolatedTopics, isolatedContext.Value);
                }
                else
                {
                    atomicTopics ??= [];
                    DispatchMulticast(operation, atomicTopics, atomicContext);
                }
            }
        }
        finally
        {
            CloseAll(atomicQueues);
            CloseAll(isolatedQueues);
            CloseAll(atomicTopics);
            CloseAll(isolatedTopics);

            if (isolatedContext.HasValue)
            {
                ReturnToPool(isolatedContext.Value.Facade);
            }
        }

        return Task.CompletedTask;
    }

    static void CloseAll(Dictionary<string, MQQueue>? queues)
    {
        if (queues == null)
        {
            return;
        }

        foreach (var queue in queues.Values)
        {
            using (queue)
            {
                queue.Close();
            }
        }
    }

    static void CloseAll(Dictionary<string, MQTopic>? topics)
    {
        if (topics == null)
        {
            return;
        }

        foreach (var topic in topics.Values)
        {
            using (topic)
            {
                topic.Close();
            }
        }
    }
}
