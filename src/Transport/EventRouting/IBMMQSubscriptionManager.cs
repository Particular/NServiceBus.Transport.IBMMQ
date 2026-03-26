namespace NServiceBus.Transport.IBMMQ;

using Extensibility;
using Logging;
using Unicast.Messages;

sealed class IBMMQSubscriptionManager(
    ILog log,
    TopicTopology topology,
    CreateMqAdminConnection createAdminConnection,
    string receiveAddress,
    bool setupInfrastructure
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

        if (!setupInfrastructure)
        {
            ValidateTopicsExist(admin, allTopicStrings);
        }

        foreach (var (eventType, topicString) in allTopicStrings)
        {
            var subscriptionName = topology.GenerateSubscriptionName(receiveAddress, topicString);
            log.DebugFormat("Subscribing to {0} topic={1} sub={2}", eventType, topicString, subscriptionName);
            using var topic = admin.EnsureSubscription(topicString, subscriptionName, receiveAddress);
            topic.Close();
        }

        return Task.CompletedTask;
    }

    void ValidateTopicsExist(MqAdminConnection admin, List<(Type EventType, string TopicString)> topicStrings)
    {
        var missing = new List<string>();

        foreach (var (eventType, topicString) in topicStrings)
        {
            if (!admin.TopicExists(topicString))
            {
                missing.Add($"'{topicString}' (for {eventType.FullName})");
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"""
                Cannot subscribe because the following topics do not exist on the queue manager: [{string.Join(", ", missing)}].
                Either create them manually, enable infrastructure setup (installers), or verify the topic strings are correct.
                """);
        }
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
