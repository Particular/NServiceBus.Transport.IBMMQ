namespace NServiceBus.Transport.IbmMq;

using Extensibility;
using Logging;
using Unicast.Messages;

sealed class IbmMqSubscriptionManager(
    ILog log,
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
            log.DebugFormat("Subscribing to {0} => {1}", eventType.MessageType, receiveAddress);
            using var topic = helper.EnsureSubscription(eventType.MessageType, receiveAddress);
            topic.Close();
        }

        connection.Disconnect();

        return Task.CompletedTask;
    }

    public Task Unsubscribe(MessageMetadata eventType, ContextBag context, CancellationToken cancellationToken = default)
    {
        log.DebugFormat("Unsubscribing from {0} => {1}", eventType.MessageType, receiveAddress);
        using var connection = createConnection();
        var helper = createFacade(connection);
        helper.RemoveSubscription(eventType.MessageType, receiveAddress);
        connection.Disconnect();

        return Task.CompletedTask;
    }
}
