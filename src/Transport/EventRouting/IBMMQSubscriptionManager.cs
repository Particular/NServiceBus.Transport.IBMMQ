namespace NServiceBus.Transport.IBMMQ;

using Extensibility;
using Logging;
using Unicast.Messages;

sealed class IBMMQSubscriptionManager(
    ILog log,
    TopicTopology topology,
    CreateQueueManagerFacade createFacade,
    CreateQueueManager createConnection,
    string receiveAddress
) : ISubscriptionManager
{

    public Task SubscribeAll(MessageMetadata[] eventTypes, ContextBag context, CancellationToken cancellationToken = default)
    {
        log.DebugFormat("SubscribeAll");
        using var connection = createConnection();
        var helper = createFacade(connection);
        foreach (var eventType in eventTypes)
        {
            foreach (var topicString in topology.GetSubscriptionTopicStrings(eventType.MessageType))
            {
                var subscriptionName = topology.GenerateSubscriptionName(receiveAddress, topicString);
                log.DebugFormat("Subscribing to {0} topic={1} sub={2}", eventType.MessageType, topicString, subscriptionName);
                using var topic = helper.EnsureSubscription(topicString, subscriptionName, receiveAddress);
                topic.Close();
            }
        }

        connection.Disconnect();

        return Task.CompletedTask;
    }

    public Task Unsubscribe(MessageMetadata eventType, ContextBag context, CancellationToken cancellationToken = default)
    {
        log.DebugFormat("Unsubscribing from {0} => {1}", eventType.MessageType, receiveAddress);
        using var connection = createConnection();
        var helper = createFacade(connection);

        foreach (var topicString in topology.GetSubscriptionTopicStrings(eventType.MessageType))
        {
            var subscriptionName = topology.GenerateSubscriptionName(receiveAddress, topicString);
            helper.RemoveSubscription(subscriptionName);
        }

        connection.Disconnect();

        return Task.CompletedTask;
    }
}
