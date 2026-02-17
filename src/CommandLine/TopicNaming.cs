namespace NServiceBus.Transport.IbmMq.CommandLine;

/// <summary>
/// Generates IBM MQ topic names and strings from .NET type names.
/// Mirrors the naming logic from the transport's TopicNaming class.
/// </summary>
static class TopicNaming
{
    public static string GenerateTopicName(string eventTypeName, string topicPrefix)
    {
        var fullName = eventTypeName.Replace('+', '.').ToUpperInvariant();
        var name = $"{topicPrefix.ToUpperInvariant()}.{fullName}";

        if (name.Length > 48)
        {
            throw new InvalidOperationException(
                $"Generated topic name '{name}' is {name.Length} characters, which exceeds the IBM MQ 48-character limit. " +
                "Use a shorter event type name or a shorter topic prefix.");
        }

        return name;
    }

    public static string GenerateTopicString(string eventTypeName, string topicPrefix) =>
        $"{topicPrefix.ToLowerInvariant()}/{eventTypeName.Replace('+', '/').ToLowerInvariant()}/";

    public static string GenerateSubscriptionName(string endpointName, string topicString) =>
        $"{endpointName}:{topicString}";
}
