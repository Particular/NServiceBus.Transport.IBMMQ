namespace NServiceBus.Transport.IbmMq;

using IBM.WMQ;
using NServiceBus.Extensibility;
using NServiceBus.Logging;
using NServiceBus.Unicast.Messages;

class IbmMqSubscriptionManager(
    Func<MQQueueManager, IbmMqHelper> createHelper,
    MQConnectionPool connectionPool,
    string receiveAddress
) : ISubscriptionManager
{
    readonly ILog Log = LogManager.GetLogger<IbmMqSubscriptionManager>();

    public Task SubscribeAll(MessageMetadata[] eventTypes, ContextBag context, CancellationToken cancellationToken = default)
    {
        Log.DebugFormat("SubscribeAll");
        var connection = connectionPool.Lease();
        try
        {
            var helper = createHelper(connection);
            foreach (var eventType in eventTypes)
            {
                Log.DebugFormat("Subscribing to {0} => {1}", eventType.MessageType, receiveAddress);
                using var topic = helper.EnsureSubscription(eventType.MessageType, receiveAddress);
                topic.Close();
            }
        }
        finally
        {
            connectionPool.Return(connection);
        }

        return Task.CompletedTask;
    }

    public Task Unsubscribe(MessageMetadata eventType, ContextBag context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}