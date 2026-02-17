namespace NServiceBus.Transport.IbmMq;

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
    /// <param name="topicPrefix">Prefix for topic names and strings. Default: "DEV".</param>
    public static TopicTopology Flat(string topicPrefix = "DEV") => new FlatTopicTopology(topicPrefix);

    internal abstract IReadOnlyList<TopicDestination> GetPublishDestinations(Type eventType);

    internal abstract IReadOnlyList<string> GetSubscriptionTopicStrings(Type eventType);

    internal virtual string GenerateSubscriptionName(string endpointName, string topicString) =>
        TopicNaming.GenerateSubscriptionName(endpointName, topicString);
}
