namespace NServiceBus.Transport.IBMMQ.CommandLine.Tests;

using NUnit.Framework;

[TestFixture]
public class TopicNamingTests
{
    [Test]
    public void Topic_name_uses_upper_case_with_prefix()
    {
        var name = TopicNaming.GenerateTopicName("MyNamespace.MyEvent", "DEV");

        Assert.That(name, Is.EqualTo("DEV.MYNAMESPACE.MYEVENT"));
    }

    [Test]
    public void Topic_string_uses_lower_case_with_prefix()
    {
        var topicString = TopicNaming.GenerateTopicString("MyNamespace.MyEvent", "DEV");

        Assert.That(topicString, Is.EqualTo("dev/mynamespace.myevent/"));
    }

    [Test]
    public void Topic_string_ends_with_trailing_slash()
    {
        var topicString = TopicNaming.GenerateTopicString("MyEvent", "DEV");

        Assert.That(topicString, Does.EndWith("/"));
    }

    [Test]
    public void Nested_type_uses_dot_separator_in_topic_name()
    {
        var name = TopicNaming.GenerateTopicName("Outer+Inner", "DEV");

        Assert.That(name, Is.EqualTo("DEV.OUTER.INNER"));
    }

    [Test]
    public void Nested_type_uses_slash_separator_in_topic_string()
    {
        var topicString = TopicNaming.GenerateTopicString("Outer+Inner", "DEV");

        Assert.That(topicString, Is.EqualTo("dev/outer/inner/"));
    }

    [Test]
    public void Subscription_name_combines_endpoint_and_topic_string()
    {
        var subscriptionName = TopicNaming.GenerateSubscriptionName("my-endpoint", "dev/some.event/");

        Assert.That(subscriptionName, Is.EqualTo("my-endpoint:dev/some.event/"));
    }

    [Test]
    public void Custom_prefix_is_applied_to_topic_name()
    {
        var name = TopicNaming.GenerateTopicName("MyEvent", "PROD");

        Assert.That(name, Does.StartWith("PROD."));
    }

    [Test]
    public void Custom_prefix_is_applied_to_topic_string()
    {
        var topicString = TopicNaming.GenerateTopicString("MyEvent", "PROD");

        Assert.That(topicString, Does.StartWith("prod/"));
    }

    [Test]
    public void Topic_name_prefix_is_always_upper_case()
    {
        var name = TopicNaming.GenerateTopicName("MyEvent", "dev");

        Assert.That(name, Does.StartWith("DEV."));
    }

    [Test]
    public void Topic_string_prefix_is_always_lower_case()
    {
        var topicString = TopicNaming.GenerateTopicString("MyEvent", "DEV");

        Assert.That(topicString, Does.StartWith("dev/"));
    }

    [Test]
    public void Topic_name_throws_when_exceeding_48_characters()
    {
        Assert.That(
            () => TopicNaming.GenerateTopicName("VeryLongEventNameThatWillExceedTheFortyEightCharacterLimit", "DEV"),
            Throws.InvalidOperationException.With.Message.Contains("48-character limit"));
    }

    [Test]
    public void Topic_name_at_exactly_48_characters_does_not_throw()
    {
        var typeName = new string('A', 44);

        Assert.That(() => TopicNaming.GenerateTopicName(typeName, "DEV"), Throws.Nothing);
        Assert.That(TopicNaming.GenerateTopicName(typeName, "DEV").Length, Is.EqualTo(48));
    }
}
