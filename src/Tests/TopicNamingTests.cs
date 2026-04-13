namespace NServiceBus.Transport.IBMMQ.Tests;

using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;

[TestFixture]
public class TopicNamingTests
{
    readonly TopicNaming naming = new();

    [Test]
    public void Topic_name_uses_upper_case_with_prefix()
    {
        var name = naming.GenerateTopicName(typeof(Evt));

        Assert.That(name, Is.EqualTo("DEV.NSERVICEBUS.TRANSPORT.IBMMQ.TESTS.EVT"));
    }

    [Test]
    public void Topic_string_uses_lower_case_with_prefix()
    {
        var topicString = naming.GenerateTopicString(typeof(Evt));

        Assert.That(topicString, Is.EqualTo("dev/nservicebus.transport.ibmmq.tests.evt/"));
    }

    [Test]
    public void Nested_type_uses_dot_separator_in_topic_name()
    {
        var name = naming.GenerateTopicName(typeof(O.I));

        Assert.That(name, Does.Contain(".O.I"));
        Assert.That(name, Does.Not.Contain("+"));
    }

    [Test]
    public void Nested_type_uses_slash_separator_in_topic_string()
    {
        var topicString = naming.GenerateTopicString(typeof(O.I));

        Assert.That(topicString, Does.Contain("o/i/"));
        Assert.That(topicString, Does.Not.Contain("+"));
    }

    [Test]
    public void Subscription_name_combines_endpoint_and_topic_string()
    {
        var subscriptionName = naming.GenerateSubscriptionName("my-endpoint", "dev/some.event/");

        Assert.That(subscriptionName, Is.EqualTo("my-endpoint:dev/some.event/"));
    }

    [Test]
    public void Custom_prefix_is_applied_to_topic_name()
    {
        var custom = new TopicNaming("PROD");

        var name = custom.GenerateTopicName(typeof(Evt));

        Assert.That(name, Does.StartWith("PROD."));
    }

    [Test]
    public void Custom_prefix_is_applied_to_topic_string()
    {
        var custom = new TopicNaming("PROD");

        var topicString = custom.GenerateTopicString(typeof(Evt));

        Assert.That(topicString, Does.StartWith("prod/"));
    }

    [Test]
    public void Topic_name_throws_when_exceeding_48_characters()
    {
        Assert.That(
            () => naming.GenerateTopicName(typeof(VeryLongEventNameThatWillExceedTheFortyEightCharacterLimit)),
            Throws.InvalidOperationException.With.Message.Contains("48-character limit"));
    }
}

[TestFixture]
public class ShortenedTopicNamingTests
{
    readonly ShortenedTopicNaming naming = new();

    [Test]
    public void Short_topic_name_is_not_truncated()
    {
        var name = naming.GenerateTopicName(typeof(Evt));

        Assert.That(name, Has.Length.LessThanOrEqualTo(48));
        Assert.That(name, Does.Not.Contain("_"));
    }

    [Test]
    public void Long_topic_name_is_truncated_to_48_characters()
    {
        var name = naming.GenerateTopicName(typeof(VeryLongEventNameThatWillExceedTheFortyEightCharacterLimit));

        Assert.That(name, Has.Length.EqualTo(48));
    }

    [Test]
    public void Long_topic_name_ends_with_hash_suffix()
    {
        var name = naming.GenerateTopicName(typeof(VeryLongEventNameThatWillExceedTheFortyEightCharacterLimit));
        var fullName = (typeof(VeryLongEventNameThatWillExceedTheFortyEightCharacterLimit).FullName ?? "").Replace('+', '.').ToUpperInvariant();
        var raw = $"DEV.{fullName}";
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..8];

        Assert.That(name, Does.EndWith($"_{expectedHash}"));
    }

    [Test]
    public void Truncated_topic_name_is_deterministic()
    {
        var name1 = naming.GenerateTopicName(typeof(VeryLongEventNameThatWillExceedTheFortyEightCharacterLimit));
        var name2 = naming.GenerateTopicName(typeof(VeryLongEventNameThatWillExceedTheFortyEightCharacterLimit));

        Assert.That(name1, Is.EqualTo(name2));
    }

    [Test]
    public void Topic_string_is_not_shortened()
    {
        var topicString = naming.GenerateTopicString(typeof(VeryLongEventNameThatWillExceedTheFortyEightCharacterLimit));

        Assert.That(topicString, Does.StartWith("dev/"));
        Assert.That(topicString, Does.EndWith("/"));
    }
}
