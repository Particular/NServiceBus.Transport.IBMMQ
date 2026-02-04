using IBM.WMQ;
using NServiceBus.Extensibility;
using NServiceBus.Unicast.Messages;

namespace NServiceBus.Transport.IbmMq;

internal class IbmMqSubscriptionManager(MQQueueManager queueManagerInstance, string receiveAddress)
    : ISubscriptionManager
{
    private readonly IbmMqHelper _ibmMqHelper = new(queueManagerInstance);

    public Task SubscribeAll(MessageMetadata[] eventTypes, ContextBag context, CancellationToken cancellationToken = default)
    {
        foreach(var eventType in eventTypes)
        {
            _ibmMqHelper.EnsureSubscription(eventType.MessageType, receiveAddress);
        }

        return Task.CompletedTask;
    }

    public Task Unsubscribe(MessageMetadata eventType, ContextBag context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
