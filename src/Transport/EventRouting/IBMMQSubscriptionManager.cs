namespace NServiceBus.Transport.IBMMQ;

using Extensibility;
using Logging;
using Unicast.Messages;

sealed class IBMMQSubscriptionManager(
    ILog log,
    TopicTopology topology,
    CreateMqAdminConnection createAdminConnection,
    string receiveAddress
) : ISubscriptionManager
{

    public Task SubscribeAll(MessageMetadata[] eventTypes, ContextBag context, CancellationToken cancellationToken = default)
    {
        log.DebugFormat("SubscribeAll");
        using var admin = createAdminConnection();

        var allTopicStrings = eventTypes
            .SelectMany(et => topology.GetSubscriptionTopicStrings(et.MessageType)
                .Select(ts => (EventType: et.MessageType, TopicString: ts)))
            .ToList();

        foreach (var (eventType, topicString) in allTopicStrings)
        {
            var subscriptionName = topology.GenerateSubscriptionName(receiveAddress, topicString);
            log.DebugFormat("Subscribing to {0} topic={1} sub={2}", eventType, topicString, subscriptionName);
            using var topic = admin.EnsureSubscription(topicString, subscriptionName, receiveAddress);
            topic.Close();
        }

        return Task.CompletedTask;
    }

    public Task Unsubscribe(MessageMetadata eventType, ContextBag context, CancellationToken cancellationToken = default)
    {
        log.DebugFormat("Unsubscribing from {0} => {1}", eventType.MessageType, receiveAddress);
        using var admin = createAdminConnection();

        foreach (var topicString in topology.GetSubscriptionTopicStrings(eventType.MessageType))
        {
            var subscriptionName = topology.GenerateSubscriptionName(receiveAddress, topicString);
            admin.RemoveSubscription(subscriptionName);
        }

        return Task.CompletedTask;
    }
}
