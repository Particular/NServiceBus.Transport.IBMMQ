namespace NServiceBus.Transport.IBMMQ;

/// <summary>
/// Controls how events are mapped to IBM MQ topics for pub/sub.
/// </summary>
public abstract class TopicTopology
{
    /// <summary>
    /// Creates a flat topology where each concrete event type gets one topic.
    /// Supports full polymorphism via subscriber-side fan-out — subscribing to a base class or
    /// interface also subscribes to all concrete descendants found in loaded assemblies.
    /// </summary>
    public static TopicTopology TopicPerEvent() => new TopicPerEventTopology();

    internal TopicNaming Naming { get; set; } = new();

    internal abstract IReadOnlyList<TopicDestination> GetPublishDestinations(Type eventType);

    internal abstract IReadOnlyList<string> GetSubscriptionTopicStrings(Type eventType);

    internal virtual string GenerateSubscriptionName(string endpointName, string topicString) =>
        Naming.GenerateSubscriptionName(endpointName, topicString);
}
