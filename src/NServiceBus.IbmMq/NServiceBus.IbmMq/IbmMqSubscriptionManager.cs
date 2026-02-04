using IBM.WMQ;
using NServiceBus.Extensibility;
using NServiceBus.Unicast.Messages;

namespace NServiceBus.Transport.IbmMq;

internal class IbmMqSubscriptionManager : ISubscriptionManager
{
    private readonly IbmMqHelper _ibmMqHelper;
    private readonly string _receiveAddress;

    public IbmMqSubscriptionManager(MQQueueManager queueManagerInstance, string receiveAddress)
    {
        _ibmMqHelper = new IbmMqHelper(queueManagerInstance);
        _receiveAddress = receiveAddress;
    }

    public Task SubscribeAll(MessageMetadata[] eventTypes, ContextBag context, CancellationToken cancellationToken = default)
    {
        foreach(var eventType in eventTypes)
        {
            _ibmMqHelper.EnsureSubscription(eventType.MessageType, _receiveAddress);
        }

        return Task.CompletedTask;
    }

    public Task Unsubscribe(MessageMetadata eventType, ContextBag context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
