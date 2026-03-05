namespace NServiceBus.Transport.IBMMQ.CommandLine;

static class TopicNaming
{
    const int MaxTopicNameLength = 48;

    public static string GenerateTopicName(string eventTypeName, string topicPrefix)
    {
        var fullName = eventTypeName.Replace('+', '.').ToUpperInvariant();
        var name = $"{topicPrefix.ToUpperInvariant()}.{fullName}";

        if (name.Length > MaxTopicNameLength)
        {
            throw new InvalidOperationException(
                $"Generated topic name '{name}' is {name.Length} characters, which exceeds the IBM MQ {MaxTopicNameLength}-character limit. " +
                "Use a shorter event type name or a shorter topic prefix.");
        }

        return name;
    }

    public static string GenerateTopicString(string eventTypeName, string topicPrefix) =>
        $"{topicPrefix.ToLowerInvariant()}/{eventTypeName.Replace('+', '/').ToLowerInvariant()}/";

    public static string GenerateSubscriptionName(string endpointName, string topicString) =>
        $"{endpointName}:{topicString}";
}
