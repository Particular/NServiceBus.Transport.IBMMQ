namespace NServiceBus.Transport.IbmMq;

using IBM.WMQ;

sealed class AtomicMessageDispatcher(MqQueueManagerFacade sendFacade, CreateQueueManagerFacade createFacade)
    : MessageDispatcher(sendFacade)
{
    protected override DispatchContext ResolveContext(TransportTransaction transaction)
    {
        var receiveConnection = transaction.Get<MQQueueManager>();
        return new(createFacade(receiveConnection), MQC.MQPMO_FAIL_IF_QUIESCING | MQC.MQPMO_SYNCPOINT);
    }
}
