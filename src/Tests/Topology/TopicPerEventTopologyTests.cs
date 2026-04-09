namespace NServiceBus.Transport.IBMMQ.Tests.Topology;

using NUnit.Framework;

[TestFixture]
public class TopicPerEventTopologyTests
{
    readonly TopicNaming naming = new ShortenedTopicNaming();
    readonly TopicTopology topology;

    public TopicPerEventTopologyTests()
    {
        topology = new TopicTopology
        {
            Naming = naming,
            // Disable the guard so we can test subscription results directly
            ThrowOnPolymorphicSubscription = false
        };
    }

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

        Assert.That(destinations[0].TopicName, Is.EqualTo(naming.GenerateTopicName(typeof(ConcreteEvent))));
    }

    [Test]
    public void Publish_destination_has_correct_topic_string()
    {
        var destinations = topology.GetPublishDestinations(typeof(ConcreteEvent));

        Assert.That(destinations[0].TopicString, Is.EqualTo(naming.GenerateTopicString(typeof(ConcreteEvent))));
    }

    [Test]
    public void Subscribe_to_concrete_leaf_returns_only_that_type()
    {
        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(ConcreteEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(1));
        Assert.That(topicStrings[0], Is.EqualTo(naming.GenerateTopicString(typeof(ConcreteEvent))));
    }

    [Test]
    public void Subscribe_to_base_class_returns_only_that_type()
    {
        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(BaseEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(1));
        Assert.That(topicStrings[0], Is.EqualTo(naming.GenerateTopicString(typeof(BaseEvent))));
    }

    [Test]
    public void Subscribe_to_interface_returns_only_that_type()
    {
        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(IMyEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(1));
        Assert.That(topicStrings[0], Is.EqualTo(naming.GenerateTopicString(typeof(IMyEvent))));
    }

    [Test]
    public void Subscribe_to_orphan_interface_returns_only_that_type()
    {
        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(IOrphanEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(1));
        Assert.That(topicStrings[0], Is.EqualTo(naming.GenerateTopicString(typeof(IOrphanEvent))));
    }

    [Test]
    public void Custom_prefix_is_used()
    {
        var customNaming = new ShortenedTopicNaming("PROD");
        var topo = new TopicTopology { Naming = customNaming };

        var destinations = topo.GetPublishDestinations(typeof(ConcreteEvent));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(destinations[0].TopicName, Does.StartWith("PROD."));
            Assert.That(destinations[0].TopicString, Does.StartWith("prod/"));
        }
    }

    [Test]
    public void Subscribe_results_are_cached()
    {
        var result1 = topology.GetSubscriptionTopicStrings(typeof(ConcreteEvent));
        var result2 = topology.GetSubscriptionTopicStrings(typeof(ConcreteEvent));

        Assert.That(result1, Is.SameAs(result2));
    }
}

[TestFixture]
public class TopicPerEventTopologyPolymorphicGuardTests
{
    readonly TopicNaming naming = new ShortenedTopicNaming();

    TopicTopology CreateTopology(bool throwOnPolymorphic = true) => new()
    {
        Naming = naming,
        ThrowOnPolymorphicSubscription = throwOnPolymorphic
    };

    [Test]
    public void Guard_enabled_throws_when_subscribing_to_interface_with_implementors()
    {
        var topology = CreateTopology(throwOnPolymorphic: true);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            topology.GetSubscriptionTopicStrings(typeof(IMyEvent)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex!.Message, Does.Contain(nameof(IMyEvent)));
            Assert.That(ex.Message, Does.Contain(nameof(TopicTopology.ThrowOnPolymorphicSubscription)));
        }
    }

    [Test]
    public void Guard_enabled_throws_when_subscribing_to_base_class_with_descendants()
    {
        var topology = CreateTopology(throwOnPolymorphic: true);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            topology.GetSubscriptionTopicStrings(typeof(BaseEvent)));

        Assert.That(ex!.Message, Does.Contain(nameof(BaseEvent)));
    }

    [Test]
    public void Guard_error_message_mentions_SubscribeTo()
    {
        var topology = CreateTopology(throwOnPolymorphic: true);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            topology.GetSubscriptionTopicStrings(typeof(IMyEvent)));

        Assert.That(ex!.Message, Does.Contain(nameof(TopicTopology.SubscribeTo)));
    }

    [Test]
    public void Guard_enabled_allows_subscribing_to_concrete_leaf_type()
    {
        var topology = CreateTopology(throwOnPolymorphic: true);

        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(ConcreteEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(1));
    }

    [Test]
    public void Guard_enabled_allows_subscribing_to_orphan_interface()
    {
        var topology = CreateTopology(throwOnPolymorphic: true);

        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(IOrphanEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(1));
    }

    [Test]
    public void Guard_disabled_allows_subscribing_to_interface_with_implementors()
    {
        var topology = CreateTopology(throwOnPolymorphic: false);

        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(IMyEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(1));
        Assert.That(topicStrings[0], Is.EqualTo(naming.GenerateTopicString(typeof(IMyEvent))));
    }

    [Test]
    public void Guard_disabled_allows_subscribing_to_base_class_with_descendants()
    {
        var topology = CreateTopology(throwOnPolymorphic: false);

        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(BaseEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(1));
        Assert.That(topicStrings[0], Is.EqualTo(naming.GenerateTopicString(typeof(BaseEvent))));
    }

    [Test]
    public void Guard_is_enabled_by_default()
    {
        var topology = new TopicTopology();

        Assert.That(topology.ThrowOnPolymorphicSubscription, Is.True);
    }

    [Test]
    public void Guard_is_bypassed_when_explicit_routes_exist()
    {
        var topology = CreateTopology(throwOnPolymorphic: true);
        topology.SubscribeTo<IMyEvent>(naming.GenerateTopicString(typeof(ConcreteEvent)));

        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(IMyEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(1));
        Assert.That(topicStrings[0], Is.EqualTo(naming.GenerateTopicString(typeof(ConcreteEvent))));
    }
}

[TestFixture]
public class TopicPerEventTopologySubscribeToTests
{
    readonly TopicNaming naming = new ShortenedTopicNaming();

    TopicTopology CreateTopology() => new() { Naming = naming };

    [Test]
    public void Single_route_returns_that_topic()
    {
        var topology = CreateTopology();
        topology.SubscribeTo<IMyEvent>(naming.GenerateTopicString(typeof(ConcreteEvent)));

        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(IMyEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(1));
        Assert.That(topicStrings[0], Is.EqualTo(naming.GenerateTopicString(typeof(ConcreteEvent))));
    }

    [Test]
    public void Multiple_routes_returns_all_topics()
    {
        var topology = CreateTopology();
        topology.SubscribeTo<IMyEvent>(naming.GenerateTopicString(typeof(BaseEvent)));
        topology.SubscribeTo<IMyEvent>(naming.GenerateTopicString(typeof(ConcreteEvent)));

        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(IMyEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(2));
        Assert.That(topicStrings, Does.Contain(naming.GenerateTopicString(typeof(BaseEvent))));
        Assert.That(topicStrings, Does.Contain(naming.GenerateTopicString(typeof(ConcreteEvent))));
    }

    [Test]
    public void Route_does_not_include_original_type_topic()
    {
        var topology = CreateTopology();
        topology.SubscribeTo<IMyEvent>(naming.GenerateTopicString(typeof(ConcreteEvent)));

        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(IMyEvent));

        Assert.That(topicStrings, Does.Not.Contain(naming.GenerateTopicString(typeof(IMyEvent))));
    }

    [Test]
    public void Route_does_not_affect_other_types()
    {
        var topology = CreateTopology();
        topology.ThrowOnPolymorphicSubscription = false;
        topology.SubscribeTo<IMyEvent>(naming.GenerateTopicString(typeof(ConcreteEvent)));

        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(BaseEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(1));
        Assert.That(topicStrings[0], Is.EqualTo(naming.GenerateTopicString(typeof(BaseEvent))));
    }

    [Test]
    public void Results_are_cached()
    {
        var topology = CreateTopology();
        topology.SubscribeTo<IMyEvent>(naming.GenerateTopicString(typeof(ConcreteEvent)));

        var result1 = topology.GetSubscriptionTopicStrings(typeof(IMyEvent));
        var result2 = topology.GetSubscriptionTopicStrings(typeof(IMyEvent));

        Assert.That(result1, Is.SameAs(result2));
    }

    [Test]
    public void Multiple_routes_with_base_class()
    {
        var topology = CreateTopology();
        topology.SubscribeTo<BaseEvent>(naming.GenerateTopicString(typeof(ConcreteEvent)));
        topology.SubscribeTo<BaseEvent>(naming.GenerateTopicString(typeof(DerivedFromAbstractEvent)));

        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(BaseEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(2));
        Assert.That(topicStrings, Does.Contain(naming.GenerateTopicString(typeof(ConcreteEvent))));
        Assert.That(topicStrings, Does.Contain(naming.GenerateTopicString(typeof(DerivedFromAbstractEvent))));
    }

    [Test]
    public void Raw_string_route()
    {
        var topology = CreateTopology();
        topology.SubscribeTo<IMyEvent>("custom/orders/accepted/");

        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(IMyEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(1));
        Assert.That(topicStrings[0], Is.EqualTo("custom/orders/accepted/"));
    }

    [Test]
    public void Multiple_raw_string_routes()
    {
        var topology = CreateTopology();
        topology.SubscribeTo<IMyEvent>("custom/orders/accepted/");
        topology.SubscribeTo<IMyEvent>("custom/orders/declined/");

        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(IMyEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(2));
        Assert.That(topicStrings, Does.Contain("custom/orders/accepted/"));
        Assert.That(topicStrings, Does.Contain("custom/orders/declined/"));
    }

    [Test]
    public void Generic_two_type_overload_uses_topic_type_naming()
    {
        var topology = CreateTopology();
        topology.SubscribeTo<IMyEvent, ConcreteEvent>();

        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(IMyEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(1));
        Assert.That(topicStrings[0], Is.EqualTo(naming.GenerateTopicString(typeof(ConcreteEvent))));
    }

    [Test]
    public void Non_generic_type_overload_uses_topic_type_naming()
    {
        var topology = CreateTopology();
        topology.SubscribeTo(typeof(IMyEvent), typeof(ConcreteEvent));

        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(IMyEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(1));
        Assert.That(topicStrings[0], Is.EqualTo(naming.GenerateTopicString(typeof(ConcreteEvent))));
    }

    [Test]
    public void Non_generic_string_overload()
    {
        var topology = CreateTopology();
        topology.SubscribeTo(typeof(IMyEvent), "custom/topic/");

        var topicStrings = topology.GetSubscriptionTopicStrings(typeof(IMyEvent));

        Assert.That(topicStrings, Has.Count.EqualTo(1));
        Assert.That(topicStrings[0], Is.EqualTo("custom/topic/"));
    }
}

[TestFixture]
public class TopicPerEventTopologyPublishToTests
{
    readonly TopicNaming naming = new ShortenedTopicNaming();

    TopicTopology CreateTopology() => new() { Naming = naming };

    [Test]
    public void Route_redirects_to_target_topic()
    {
        var topology = CreateTopology();
        topology.PublishTo<ConcreteEvent>(naming.GenerateTopicString(typeof(IMyEvent)));

        var destinations = topology.GetPublishDestinations(typeof(ConcreteEvent));

        Assert.That(destinations, Has.Count.EqualTo(1));
        Assert.That(destinations[0].TopicString, Is.EqualTo(naming.GenerateTopicString(typeof(IMyEvent))));
    }

    [Test]
    public void Route_does_not_affect_unmapped_types()
    {
        var topology = CreateTopology();
        topology.PublishTo<ConcreteEvent>(naming.GenerateTopicString(typeof(IMyEvent)));

        var destinations = topology.GetPublishDestinations(typeof(BaseEvent));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(destinations[0].TopicName, Is.EqualTo(naming.GenerateTopicName(typeof(BaseEvent))));
            Assert.That(destinations[0].TopicString, Is.EqualTo(naming.GenerateTopicString(typeof(BaseEvent))));
        }
    }

    [Test]
    public void Multiple_types_to_same_shared_topic()
    {
        var topology = CreateTopology();
        topology.PublishTo<ConcreteEvent>(naming.GenerateTopicString(typeof(IMyEvent)));
        topology.PublishTo<BaseEvent>(naming.GenerateTopicString(typeof(IMyEvent)));

        var dest1 = topology.GetPublishDestinations(typeof(ConcreteEvent));
        var dest2 = topology.GetPublishDestinations(typeof(BaseEvent));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dest1[0].TopicString, Is.EqualTo(naming.GenerateTopicString(typeof(IMyEvent))));
            Assert.That(dest2[0].TopicString, Is.EqualTo(naming.GenerateTopicString(typeof(IMyEvent))));
        }
    }

    [Test]
    public void Multiple_calls_are_additive()
    {
        var topology = CreateTopology();
        topology.PublishTo<ConcreteEvent>(naming.GenerateTopicString(typeof(BaseEvent)));
        topology.PublishTo<ConcreteEvent>(naming.GenerateTopicString(typeof(IMyEvent)));

        var destinations = topology.GetPublishDestinations(typeof(ConcreteEvent));

        Assert.That(destinations, Has.Count.EqualTo(2));
        Assert.That(destinations.Select(d => d.TopicString), Does.Contain(naming.GenerateTopicString(typeof(BaseEvent))));
        Assert.That(destinations.Select(d => d.TopicString), Does.Contain(naming.GenerateTopicString(typeof(IMyEvent))));
    }

    [Test]
    public void Results_are_cached()
    {
        var topology = CreateTopology();
        topology.PublishTo<ConcreteEvent>(naming.GenerateTopicString(typeof(IMyEvent)));

        var result1 = topology.GetPublishDestinations(typeof(ConcreteEvent));
        var result2 = topology.GetPublishDestinations(typeof(ConcreteEvent));

        Assert.That(result1, Is.SameAs(result2));
    }

    [Test]
    public void Raw_string_route_has_null_topic_name()
    {
        var topology = CreateTopology();
        topology.PublishTo<ConcreteEvent>("legacy/order/events/");

        var destinations = topology.GetPublishDestinations(typeof(ConcreteEvent));

        Assert.That(destinations, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(destinations[0].TopicString, Is.EqualTo("legacy/order/events/"));
            Assert.That(destinations[0].TopicName, Is.Null);
        }
    }

    [Test]
    public void Non_generic_string_overload_has_null_topic_name()
    {
        var topology = CreateTopology();
        topology.PublishTo(typeof(ConcreteEvent), "legacy/order/events/");

        var destinations = topology.GetPublishDestinations(typeof(ConcreteEvent));

        Assert.That(destinations, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(destinations[0].TopicString, Is.EqualTo("legacy/order/events/"));
            Assert.That(destinations[0].TopicName, Is.Null);
        }
    }

    [Test]
    public void Explicit_topic_name_overload()
    {
        var topology = CreateTopology();
        topology.PublishTo<ConcreteEvent>("MY.TOPIC", "my/topic/");

        var destinations = topology.GetPublishDestinations(typeof(ConcreteEvent));

        Assert.That(destinations, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(destinations[0].TopicName, Is.EqualTo("MY.TOPIC"));
            Assert.That(destinations[0].TopicString, Is.EqualTo("my/topic/"));
        }
    }

    [Test]
    public void Non_generic_explicit_topic_name_overload()
    {
        var topology = CreateTopology();
        topology.PublishTo(typeof(ConcreteEvent), "MY.TOPIC", "my/topic/");

        var destinations = topology.GetPublishDestinations(typeof(ConcreteEvent));

        Assert.That(destinations, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(destinations[0].TopicName, Is.EqualTo("MY.TOPIC"));
            Assert.That(destinations[0].TopicString, Is.EqualTo("my/topic/"));
        }
    }

    [Test]
    public void Default_publish_results_are_cached()
    {
        var topology = CreateTopology();

        var result1 = topology.GetPublishDestinations(typeof(ConcreteEvent));
        var result2 = topology.GetPublishDestinations(typeof(ConcreteEvent));

        Assert.That(result1, Is.SameAs(result2));
    }
}

// Test types for subscriber fan-out scenarios
abstract class AbstractMiddleEvent : BaseEvent;
class DerivedFromAbstractEvent : AbstractMiddleEvent;

interface IOrphanEvent : IEvent;
