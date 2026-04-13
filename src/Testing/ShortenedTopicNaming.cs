using System.Security.Cryptography;
using System.Text;
using NServiceBus.Transport.IBMMQ;

/// <summary>
/// A <see cref="TopicNaming"/> subclass that shortens topic names exceeding the
/// IBM MQ 48-character limit using a SHA-256 hash suffix to preserve uniqueness.
/// </summary>
class ShortenedTopicNaming : TopicNaming
{
    public ShortenedTopicNaming(string topicPrefix = "DEV") : base(topicPrefix) => prefix = topicPrefix;

    readonly string prefix;

    public override string GenerateTopicName(Type eventType)
    {
        var fullName = (eventType.FullName ?? eventType.Name).Replace('+', '.').ToUpperInvariant();
        var name = $"{prefix.ToUpperInvariant()}.{fullName}";

        if (name.Length <= 48)
        {
            return name;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..8];
        return $"{name[..(48 - 9)]}_{hash}";
    }
}
