namespace NServiceBus.Transport.IBMMQ;

using System.Collections.Concurrent;
using IBM.WMQ;

sealed class AtomicMessageDispatcher(MqConnectionPool pool, TopicTopology topology, CreateQueueManagerFacade createFacade)
    : MessageDispatcher(pool, topology)
{
    // Reference equality is intentional — we cache per connection instance, not per connection properties
#pragma warning disable PS0025
    readonly ConcurrentDictionary<MQQueueManager, MqQueueManagerFacade> facadeCache = new();
#pragma warning restore PS0025

    public override Task Dispatch(TransportOperations outgoingMessages, TransportTransaction transaction, CancellationToken cancellationToken = default)
    {
        if (!transaction.TryGet<MQQueueManager>(out var receiveConnection))
        {
            return base.Dispatch(outgoingMessages, transaction, cancellationToken);
        }

        var atomicFacade = facadeCache.GetOrAdd(receiveConnection, qm => createFacade(qm));
        var atomicContext = new DispatchContext(atomicFacade, MQC.MQPMO_FAIL_IF_QUIESCING | MQC.MQPMO_SYNCPOINT);
        DispatchContext? isolatedContext = null;

        try
        {
            foreach (var operation in outgoingMessages.UnicastTransportOperations)
            {
                if (operation.RequiredDispatchConsistency == DispatchConsistency.Isolated)
                {
                    isolatedContext ??= ResolveContext(transaction);
                    DispatchUnicast(operation, isolatedContext.Value);
                }
                else
                {
                    DispatchUnicast(operation, atomicContext);
                }
            }

            foreach (var operation in outgoingMessages.MulticastTransportOperations)
            {
                if (operation.RequiredDispatchConsistency == DispatchConsistency.Isolated)
                {
                    isolatedContext ??= ResolveContext(transaction);
                    DispatchMulticast(operation, isolatedContext.Value);
                }
                else
                {
                    DispatchMulticast(operation, atomicContext);
                }
            }
        }
        finally
        {
            // Atomic facade is cached — don't close handles
            if (isolatedContext.HasValue)
            {
                ReturnToPool(isolatedContext.Value.Facade);
            }
        }

        return Task.CompletedTask;
    }
}
