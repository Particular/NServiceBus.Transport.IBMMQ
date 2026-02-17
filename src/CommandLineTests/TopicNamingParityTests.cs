namespace NServiceBus.Transport.IbmMq.CommandLine.Tests
{
    using System;
    using NUnit.Framework;
    using TransportTopicNaming = NServiceBus.Transport.IbmMq.TopicNaming;
    using CliTopicNaming = NServiceBus.Transport.IbmMq.CommandLine.TopicNaming;
    [TestFixture]
    public class TopicNamingParityTests
    {
        readonly TransportTopicNaming transportNaming = new();

        [Test]
        public void Topic_name_matches_transport()
        {
            var eventType = typeof(Parity.Evt);

            var transportName = transportNaming.GenerateTopicName(eventType);
            var cliName = CliTopicNaming.GenerateTopicName(eventType.FullName!, "DEV");

            Assert.That(cliName, Is.EqualTo(transportName));
        }

        [Test]
        public void Topic_name_matches_transport_for_nested_type()
        {
            var eventType = typeof(Parity.O.I);

            var transportName = transportNaming.GenerateTopicName(eventType);
            var cliName = CliTopicNaming.GenerateTopicName(eventType.FullName!, "DEV");

            Assert.That(cliName, Is.EqualTo(transportName));
        }

        [Test]
        public void Topic_string_matches_transport()
        {
            var eventType = typeof(Parity.Evt);

            var transportString = transportNaming.GenerateTopicString(eventType);
            var cliString = CliTopicNaming.GenerateTopicString(eventType.FullName!, "DEV");

            Assert.That(cliString, Is.EqualTo(transportString));
        }

        [Test]
        public void Topic_string_matches_transport_for_nested_type()
        {
            var eventType = typeof(Parity.O.I);

            var transportString = transportNaming.GenerateTopicString(eventType);
            var cliString = CliTopicNaming.GenerateTopicString(eventType.FullName!, "DEV");

            Assert.That(cliString, Is.EqualTo(transportString));
        }

        [Test]
        public void Subscription_name_matches_transport()
        {
            const string endpoint = "my-endpoint";
            var topicString = transportNaming.GenerateTopicString(typeof(Parity.Evt));

            var transportSubName = transportNaming.GenerateSubscriptionName(endpoint, topicString);
            var cliSubName = CliTopicNaming.GenerateSubscriptionName(endpoint, topicString);

            Assert.That(cliSubName, Is.EqualTo(transportSubName));
        }

        [Test]
        public void Custom_prefix_matches_transport()
        {
            var transportCustom = new TransportTopicNaming("PROD");

            var transportName = transportCustom.GenerateTopicName(typeof(Parity.Evt));
            var cliName = CliTopicNaming.GenerateTopicName(typeof(Parity.Evt).FullName!, "PROD");

            Assert.That(cliName, Is.EqualTo(transportName));
        }
    }
}

namespace Parity
{
    class Evt;

    class O
    {
        internal class I;
    }
}
