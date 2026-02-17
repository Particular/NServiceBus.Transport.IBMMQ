namespace NServiceBus.Transport.IbmMq;

static class TopicNaming
{
    internal static string GenerateTopicName(string topicPrefix, Type eventType)
    {
        var fullName = (eventType.FullName ?? eventType.Name).Replace('+', '.').ToUpperInvariant();
        var name = $"{topicPrefix.ToUpperInvariant()}.{fullName}";
        if (name.Length <= 48)
        {
            return name;
        }

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(name)))[..8];
        return $"{name[..(48 - 9)]}_{hash}";
    }

    internal static string GenerateTopicString(string topicPrefix, Type eventType)
    {
        var fullName = (eventType.FullName ?? eventType.Name).Replace('+', '/').ToLowerInvariant();
        return $"{topicPrefix.ToLowerInvariant()}/{fullName}/";
    }

    internal static string GenerateSubscriptionName(string endpointName, string topicString)
    {
        var name = $"{endpointName}:{topicString}";
        if (name.Length <= 256)
        {
            return name;
        }

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(name)))[..16];
        return $"{name[..(256 - 17)]}_{hash}";
    }
}
