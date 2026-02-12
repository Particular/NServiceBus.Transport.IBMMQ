namespace NServiceBus.Transport.IbmMq.Tests;

using System;
using System.Linq;
using NUnit.Framework;

[TestFixture]
public class TopicAndSubscriptionNamingTests
{
    [TestFixture]
    public class GenerateTopicName
    {
        [Test]
        public void Short_type_is_prefix_dot_uppercase()
        {
            var name = MqQueueManagerFacade.GenerateTopicName("DEV", typeof(Evt));

            Assert.That(name, Is.EqualTo("DEV.NSERVICEBUS.TRANSPORT.IBMMQ.TESTS.EVT"));
            Assert.That(name.Length, Is.LessThanOrEqualTo(48));
        }

        [Test]
        public void Long_type_is_hash_truncated_to_48_chars()
        {
            var name = MqQueueManagerFacade.GenerateTopicName("DEV", typeof(VeryLongEventNameThatWillExceedTheFortyEightCharacterLimit));

            Assert.That(name.Length, Is.EqualTo(48));
            Assert.That(name, Does.StartWith("DEV."));
            // Last 9 chars: underscore + 8-char hex hash
            Assert.That(name[^9], Is.EqualTo('_'));
        }

        [Test]
        public void Nested_type_replaces_plus_with_dot()
        {
            var name = MqQueueManagerFacade.GenerateTopicName("DEV", typeof(O.I));

            Assert.That(name, Is.EqualTo("DEV.NSERVICEBUS.TRANSPORT.IBMMQ.TESTS.O.I"));
        }

        [Test]
        public void Result_is_deterministic()
        {
            var name1 = MqQueueManagerFacade.GenerateTopicName("DEV", typeof(VeryLongEventNameThatWillExceedTheFortyEightCharacterLimit));
            var name2 = MqQueueManagerFacade.GenerateTopicName("DEV", typeof(VeryLongEventNameThatWillExceedTheFortyEightCharacterLimit));

            Assert.That(name1, Is.EqualTo(name2));
        }

        [Test]
        public void Custom_prefix_is_used()
        {
            var name = MqQueueManagerFacade.GenerateTopicName("PROD", typeof(Evt));

            Assert.That(name, Is.EqualTo("PROD.NSERVICEBUS.TRANSPORT.IBMMQ.TESTS.EVT"));
        }

        [Test]
        public void Prefix_is_uppercased()
        {
            var name = MqQueueManagerFacade.GenerateTopicName("myapp", typeof(Evt));

            Assert.That(name, Does.StartWith("MYAPP."));
        }
    }

    [TestFixture]
    public class GenerateTopicString
    {
        [Test]
        public void Produces_prefix_lowercase_with_trailing_slash()
        {
            var topicString = MqQueueManagerFacade.GenerateTopicString("DEV", typeof(Evt));

            Assert.That(topicString, Is.EqualTo("dev/nservicebus.transport.ibmmq.tests.evt/"));
        }

        [Test]
        public void Nested_type_replaces_plus_with_slash()
        {
            var topicString = MqQueueManagerFacade.GenerateTopicString("DEV", typeof(O.I));

            Assert.That(topicString, Is.EqualTo("dev/nservicebus.transport.ibmmq.tests.o/i/"));
        }

        [Test]
        public void Custom_prefix_is_used()
        {
            var topicString = MqQueueManagerFacade.GenerateTopicString("PROD", typeof(Evt));

            Assert.That(topicString, Is.EqualTo("prod/nservicebus.transport.ibmmq.tests.evt/"));
        }

        [Test]
        public void Prefix_is_lowercased()
        {
            var topicString = MqQueueManagerFacade.GenerateTopicString("MYAPP", typeof(Evt));

            Assert.That(topicString, Does.StartWith("myapp/"));
        }
    }

    [TestFixture]
    public class GenerateSubscriptionName
    {
        [Test]
        public void Short_name_is_endpoint_colon_topicstring()
        {
            var name = MqQueueManagerFacade.GenerateSubscriptionName("DEV", "MyEndpoint", typeof(Evt));

            Assert.That(name, Does.StartWith("MyEndpoint:dev/"));
            Assert.That(name, Does.EndWith("/"));
        }

        [Test]
        public void Long_name_is_hash_truncated_to_256_chars()
        {
            var longEndpoint = new string('A', 250);
            var name = MqQueueManagerFacade.GenerateSubscriptionName("DEV", longEndpoint, typeof(Evt));

            Assert.That(name.Length, Is.EqualTo(256));
            // Last 17 chars: underscore + 16-char hex hash
            Assert.That(name[^17], Is.EqualTo('_'));
        }

        [Test]
        public void Result_is_deterministic()
        {
            var longEndpoint = new string('A', 250);
            var name1 = MqQueueManagerFacade.GenerateSubscriptionName("DEV", longEndpoint, typeof(Evt));
            var name2 = MqQueueManagerFacade.GenerateSubscriptionName("DEV", longEndpoint, typeof(Evt));

            Assert.That(name1, Is.EqualTo(name2));
        }

        [Test]
        public void Custom_prefix_is_used_in_subscription_name()
        {
            var name = MqQueueManagerFacade.GenerateSubscriptionName("PROD", "MyEndpoint", typeof(Evt));

            Assert.That(name, Does.Contain("prod/"));
        }
    }

    [TestFixture]
    public class EventTypeHierarchy
    {
        [Test]
        public void Returns_concrete_type()
        {
            var types = MqQueueManagerFacade.GetEventTypeHierarchy(typeof(ConcreteEvent)).ToList();

            Assert.That(types, Does.Contain(typeof(ConcreteEvent)));
        }

        [Test]
        public void Returns_base_classes()
        {
            var types = MqQueueManagerFacade.GetEventTypeHierarchy(typeof(ConcreteEvent)).ToList();

            Assert.That(types, Does.Contain(typeof(BaseEvent)));
        }

        [Test]
        public void Returns_interfaces()
        {
            var types = MqQueueManagerFacade.GetEventTypeHierarchy(typeof(ConcreteEvent)).ToList();

            Assert.That(types, Does.Contain(typeof(IMyEvent)));
        }

        [Test]
        public void Excludes_IEvent()
        {
            var types = MqQueueManagerFacade.GetEventTypeHierarchy(typeof(ConcreteEvent)).ToList();

            Assert.That(types, Does.Not.Contain(typeof(IEvent)));
        }

        [Test]
        public void Excludes_IMessage()
        {
            var types = MqQueueManagerFacade.GetEventTypeHierarchy(typeof(ConcreteEvent)).ToList();

            Assert.That(types, Does.Not.Contain(typeof(IMessage)));
        }

        [Test]
        public void Excludes_object()
        {
            var types = MqQueueManagerFacade.GetEventTypeHierarchy(typeof(ConcreteEvent)).ToList();

            Assert.That(types, Does.Not.Contain(typeof(object)));
        }

        [Test]
        public void Concrete_type_is_first()
        {
            var types = MqQueueManagerFacade.GetEventTypeHierarchy(typeof(ConcreteEvent)).ToList();

            Assert.That(types[0], Is.EqualTo(typeof(ConcreteEvent)));
        }
    }
}

// Short name types for predictable FullName lengths
class Evt;
class VeryLongEventNameThatWillExceedTheFortyEightCharacterLimit;

class O
{
    internal class I;
}

interface IMyEvent : IEvent;
class BaseEvent : IMyEvent;
class ConcreteEvent : BaseEvent;
