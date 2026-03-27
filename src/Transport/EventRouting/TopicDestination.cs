namespace NServiceBus.Transport.IBMMQ;

/// <summary>
/// Represents a topic destination for publishing, with an optional admin name and topic string.
/// When <see cref="TopicName"/> is null, the topic is expected to already exist on the queue manager.
/// </summary>
readonly record struct TopicDestination(string? TopicName, string TopicString);
