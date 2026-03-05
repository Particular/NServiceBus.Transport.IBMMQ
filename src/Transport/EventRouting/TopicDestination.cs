namespace NServiceBus.Transport.IBMMQ;

/// <summary>
/// Represents a topic destination for publishing, with both the admin name and topic string.
/// </summary>
readonly record struct TopicDestination(string TopicName, string TopicString);
