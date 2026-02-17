namespace NServiceBus.Transport.IbmMq;

/// <summary>
/// Controls how event types are mapped to IBM MQ topic names and topic strings.
/// Subclass to customize naming conventions for your environment.
/// </summary>
public class TopicNaming(string topicPrefix = "DEV")
{
    /// <summary>
    /// Generates the administrative topic object name for an event type.
    /// Names are upper-cased with '.' as separator, prefixed with the topic prefix.
    /// Throws if the generated name exceeds the IBM MQ 48-character limit.
    /// Override this method to implement custom shortening strategies.
    /// </summary>
    public virtual string GenerateTopicName(Type eventType)
    {
        var fullName = (eventType.FullName ?? eventType.Name).Replace('+', '.').ToUpperInvariant();
        var name = $"{topicPrefix.ToUpperInvariant()}.{fullName}";

        if (name.Length > 48)
        {
            throw new InvalidOperationException(
                $"Generated topic name '{name}' is {name.Length} characters, which exceeds the IBM MQ 48-character limit. " +
                $"Override {nameof(GenerateTopicName)} in a custom {nameof(TopicNaming)} subclass to implement a shortening strategy.");
        }

        return name;
    }

    /// <summary>
    /// Generates the topic string used for publishing and subscribing.
    /// Strings are lower-cased and use '/' as separator.
    /// </summary>
    public virtual string GenerateTopicString(Type eventType)
    {
        var fullName = (eventType.FullName ?? eventType.Name).Replace('+', '/').ToLowerInvariant();
        return $"{topicPrefix.ToLowerInvariant()}/{fullName}/";
    }

    /// <summary>
    /// Generates the durable subscription name for an endpoint subscribing to a topic.
    /// </summary>
    public virtual string GenerateSubscriptionName(string endpointName, string topicString)
    {
        return $"{endpointName}:{topicString}";
    }
}
