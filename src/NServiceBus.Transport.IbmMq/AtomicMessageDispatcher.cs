namespace NServiceBus.Transport.IbmMq;

using IBM.WMQ;

sealed class AtomicMessageDispatcher(MqQueueManagerFacade sendFacade, CreateQueueManagerFacade createFacade)
    : MessageDispatcher(sendFacade)
{
    public override Task Dispatch(TransportOperations outgoingMessages, TransportTransaction transaction, CancellationToken cancellationToken = default)
    {
        if (!transaction.TryGet<MQQueueManager>(out var receiveConnection))
        {
            return base.Dispatch(outgoingMessages, transaction, cancellationToken);
        }

        var atomicContext = new DispatchContext(createFacade(receiveConnection), MQC.MQPMO_FAIL_IF_QUIESCING | MQC.MQPMO_SYNCPOINT);
        var isolatedContext = base.ResolveContext(transaction);

        Dictionary<string, MQQueue>? atomicQueues = null;
        Dictionary<string, MQQueue>? isolatedQueues = null;
        Dictionary<Type, MQTopic>? atomicTopics = null;
        Dictionary<Type, MQTopic>? isolatedTopics = null;

        try
        {
            foreach (var operation in outgoingMessages.UnicastTransportOperations)
            {
                if (operation.RequiredDispatchConsistency == DispatchConsistency.Isolated)
                {
                    isolatedQueues ??= [];
                    DispatchUnicast(operation, isolatedQueues, isolatedContext);
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
                    isolatedTopics ??= [];
                    DispatchMulticast(operation, isolatedTopics, isolatedContext);
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
        }

        return Task.CompletedTask;
    }

    static void CloseAll<TKey>(Dictionary<TKey, MQQueue>? queues) where TKey : notnull
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

    static void CloseAll<TKey>(Dictionary<TKey, MQTopic>? topics) where TKey : notnull
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
