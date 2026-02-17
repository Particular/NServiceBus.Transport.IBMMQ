namespace NServiceBus.Transport.IbmMq.Tests.Topology;

using NUnit.Framework;

[TestFixture]
public class FlatTopicTopologyTests
{
    readonly TopicTopology topology = TopicTopology.Flat();

    [Test]
    public void Publish_returns_single_destination_for_concrete_type()
    {
        var destinations = topology.GetPublishDestinations(typeof(ConcreteEvent));

        Assert.That(destinations, Has.Count.EqualTo(1));
    }

    [Test]
    public void Publish_destination_has_correct_topic_name()
    {
        var destinations = topology.GetPublishDestinations(typeof(ConcreteEvent));

        Assert.That(destinations[0].TopicName, Is.EqualTo(TopicNaming.GenerateTopicName("DEV", typeof(ConcreteEvent))));
    }

    [Test]
    public void Publish_destination_has_correct_topic_string()
    {
        var destinations = topology.GetPublishDestinations(typeof(ConcreteEvent));

        Assert.That(destinations[0].TopicString, Is.EqualTo(TopicNaming.GenerateTopicString("DEV", typeof(ConcreteEvent))));
    }

    [Test]
    public void Subscribe_to_concrete_leaf_returns_only_that_type()
    {
        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(ConcreteEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(1));
        Assert.That(topicStrings[0], Is.EqualTo(TopicNaming.GenerateTopicString("DEV", typeof(ConcreteEvent))));
    }

    [Test]
    public void Subscribe_to_base_class_includes_concrete_descendants()
    {
        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(BaseEvent));

        Assert.That(topicStrings, Does.Contain(TopicNaming.GenerateTopicString("DEV", typeof(BaseEvent))));
        Assert.That(topicStrings, Does.Contain(TopicNaming.GenerateTopicString("DEV", typeof(ConcreteEvent))));
    }

    [Test]
    public void Subscribe_to_interface_includes_all_concrete_implementors()
    {
        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(IMyEvent));

        Assert.That(topicStrings, Does.Contain(TopicNaming.GenerateTopicString("DEV", typeof(BaseEvent))));
        Assert.That(topicStrings, Does.Contain(TopicNaming.GenerateTopicString("DEV", typeof(ConcreteEvent))));
    }

    [Test]
    public void Subscribe_to_interface_with_no_concrete_implementors_falls_back_to_own_topic()
    {
        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(IOrphanEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(1));
        Assert.That(topicStrings[0], Is.EqualTo(TopicNaming.GenerateTopicString("DEV", typeof(IOrphanEvent))));
    }

    [Test]
    public void Custom_prefix_is_used()
    {
        var topo = TopicTopology.Flat("PROD");
        var destinations = topo.GetPublishDestinations(typeof(ConcreteEvent));

        Assert.That(destinations[0].TopicName, Does.StartWith("PROD."));
        Assert.That(destinations[0].TopicString, Does.StartWith("prod/"));
    }

    [Test]
    public void Subscribe_results_are_cached()
    {
        var result1 = topology.GetSubscriptionTopicStrings(typeof(ConcreteEvent));
        var result2 = topology.GetSubscriptionTopicStrings(typeof(ConcreteEvent));

        Assert.That(result1, Is.SameAs(result2));
    }
}

// Test types for subscriber fan-out scenarios
abstract class AbstractMiddleEvent : BaseEvent;
class DerivedFromAbstractEvent : AbstractMiddleEvent;

interface IOrphanEvent : IEvent;
