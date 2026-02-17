namespace NServiceBus.Transport.IbmMq.Tests;

using System;
using System.Collections.Generic;
using IBM.WMQ;
using NServiceBus.Performance.TimeToBeReceived;
using NServiceBus.Transport;
using NUnit.Framework;

[TestFixture]
public class IbmMqMessageConverterTests
{
    static UnicastTransportOperation CreateOperation(
        Dictionary<string, string> headers = null,
        byte[] body = null,
        DispatchProperties properties = null)
    {
        headers ??= [];
        body ??= System.Text.Encoding.UTF8.GetBytes("test-body");
        properties ??= [];

        var message = new OutgoingMessage(Guid.NewGuid().ToString(), headers, body);
        return new UnicastTransportOperation(message, "destination", properties);
    }

    [TestFixture]
    public class Expiry
    {
        [Test]
        public void Sets_expiry_from_DiscardIfNotReceivedBefore()
        {
            var ttbr = TimeSpan.FromSeconds(30);
            var properties = new DispatchProperties
            {
                DiscardIfNotReceivedBefore = new DiscardIfNotReceivedBefore(ttbr)
            };
            var operation = CreateOperation(properties: properties);

            var mqMessage = IbmMqMessageConverter.ToNative(operation);

            Assert.That(mqMessage.Expiry, Is.EqualTo(300)); // 30s * 10 = 300 tenths-of-seconds
        }

        [Test]
        public void Sets_unlimited_expiry_when_DiscardIfNotReceivedBefore_is_null()
        {
            var operation = CreateOperation(properties: []);

            var mqMessage = IbmMqMessageConverter.ToNative(operation);

            Assert.That(mqMessage.Expiry, Is.EqualTo(MQC.MQEI_UNLIMITED));
        }

        [Test]
        public void Sets_unlimited_expiry_when_properties_are_empty()
        {
            var operation = CreateOperation();

            var mqMessage = IbmMqMessageConverter.ToNative(operation);

            Assert.That(mqMessage.Expiry, Is.EqualTo(MQC.MQEI_UNLIMITED));
        }

        [Test]
        public void Converts_large_ttbr_value()
        {
            var ttbr = TimeSpan.FromHours(1);
            var properties = new DispatchProperties
            {
                DiscardIfNotReceivedBefore = new DiscardIfNotReceivedBefore(ttbr)
            };
            var operation = CreateOperation(properties: properties);

            var mqMessage = IbmMqMessageConverter.ToNative(operation);

            Assert.That(mqMessage.Expiry, Is.EqualTo(36000)); // 3600s * 10
        }
    }

    [TestFixture]
    public class MessageFields
    {
        [Test]
        public void Sets_MessageId_from_header()
        {
            var messageId = Guid.NewGuid();
            var headers = new Dictionary<string, string>
            {
                { Headers.MessageId, messageId.ToString() }
            };
            var operation = CreateOperation(headers: headers);

            var mqMessage = IbmMqMessageConverter.ToNative(operation);

            var expected = new byte[24];
            Array.Copy(messageId.ToByteArray(), expected, 16);
            Assert.That(mqMessage.MessageId, Is.EqualTo(expected));
        }

        [Test]
        public void Sets_CorrelationId_from_header()
        {
            var correlationId = Guid.NewGuid();
            var headers = new Dictionary<string, string>
            {
                { Headers.CorrelationId, correlationId.ToString() }
            };
            var operation = CreateOperation(headers: headers);

            var mqMessage = IbmMqMessageConverter.ToNative(operation);

            var expected = new byte[24];
            Array.Copy(correlationId.ToByteArray(), expected, 16);
            Assert.That(mqMessage.CorrelationId, Is.EqualTo(expected));
        }

        [Test]
        public void Sets_ReplyToQueueName_from_header()
        {
            var headers = new Dictionary<string, string>
            {
                { Headers.ReplyToAddress, "my.reply.queue" }
            };
            var operation = CreateOperation(headers: headers);

            var mqMessage = IbmMqMessageConverter.ToNative(operation);

            Assert.That(mqMessage.ReplyToQueueName, Is.EqualTo("my.reply.queue"));
        }

        [Test]
        public void Writes_message_body()
        {
            var body = System.Text.Encoding.UTF8.GetBytes("hello world");
            var operation = CreateOperation(body: body);

            var mqMessage = IbmMqMessageConverter.ToNative(operation);

            mqMessage.Seek(0);
            var actual = mqMessage.ReadBytes(mqMessage.MessageLength);
            Assert.That(actual, Is.EqualTo(body));
        }
    }

    [TestFixture]
    public class Defaults
    {
        [Test]
        public void MessageType_is_datagram()
        {
            var operation = CreateOperation();
            var mqMessage = IbmMqMessageConverter.ToNative(operation);
            Assert.That(mqMessage.MessageType, Is.EqualTo(MQC.MQMT_DATAGRAM));
        }

        [Test]
        public void Persistence_is_persistent()
        {
            var operation = CreateOperation();
            var mqMessage = IbmMqMessageConverter.ToNative(operation);
            Assert.That(mqMessage.Persistence, Is.EqualTo(MQC.MQPER_PERSISTENT));
        }

        [Test]
        public void Persistence_is_non_persistent_when_NonDurableMessage_header_is_set()
        {
            var headers = new Dictionary<string, string>
            {
                { Headers.NonDurableMessage, null }
            };
            var operation = CreateOperation(headers: headers);
            var mqMessage = IbmMqMessageConverter.ToNative(operation);
            Assert.That(mqMessage.Persistence, Is.EqualTo(MQC.MQPER_NOT_PERSISTENT));
        }

        [Test]
        public void CharacterSet_is_UTF8()
        {
            var operation = CreateOperation();
            var mqMessage = IbmMqMessageConverter.ToNative(operation);
            Assert.That(mqMessage.CharacterSet, Is.EqualTo(MQC.CODESET_UTF));
        }
    }

    [TestFixture]
    public class HeaderEscaping
    {
        [Test]
        public void Dots_in_header_names_are_escaped()
        {
            var headers = new Dictionary<string, string>
            {
                { "NServiceBus.EnclosedMessageTypes", "MyEvent" }
            };
            var operation = CreateOperation(headers: headers);

            var mqMessage = IbmMqMessageConverter.ToNative(operation);

            var value = mqMessage.GetStringProperty("NServiceBus_x002EEnclosedMessageTypes");
            Assert.That(value, Is.EqualTo("MyEvent"));
        }

        [Test]
        public void Hyphens_in_header_names_are_escaped()
        {
            var headers = new Dictionary<string, string>
            {
                { "my-header", "value" }
            };
            var operation = CreateOperation(headers: headers);

            var mqMessage = IbmMqMessageConverter.ToNative(operation);

            var value = mqMessage.GetStringProperty("my_x002Dheader");
            Assert.That(value, Is.EqualTo("value"));
        }

        [Test]
        public void Underscores_in_header_names_are_doubled()
        {
            var headers = new Dictionary<string, string>
            {
                { "my_header", "value" }
            };
            var operation = CreateOperation(headers: headers);

            var mqMessage = IbmMqMessageConverter.ToNative(operation);

            var value = mqMessage.GetStringProperty("my__header");
            Assert.That(value, Is.EqualTo("value"));
        }
    }

    [TestFixture]
    public class EmptyHeaderManifest
    {
        [Test]
        public void Empty_header_values_are_tracked_in_manifest()
        {
            var headers = new Dictionary<string, string>
            {
                { "NormalHeader", "has-value" },
                { "EmptyHeader", "" }
            };
            var operation = CreateOperation(headers: headers);

            var mqMessage = IbmMqMessageConverter.ToNative(operation);

            var manifest = mqMessage.GetStringProperty("nsbhdrs");
            Assert.That(manifest, Does.Contain("NormalHeader"));
            Assert.That(manifest, Does.Contain("EmptyHeader"));

            var emptyManifest = mqMessage.GetStringProperty("nsbempty");
            Assert.That(emptyManifest, Does.Contain("EmptyHeader"));
            Assert.That(emptyManifest, Does.Not.Contain("NormalHeader"));
        }

        [Test]
        public void No_nsbempty_property_when_no_empty_headers()
        {
            var headers = new Dictionary<string, string>
            {
                { "Header1", "value1" }
            };
            var operation = CreateOperation(headers: headers);

            var mqMessage = IbmMqMessageConverter.ToNative(operation);

            Assert.Throws<MQException>(() => mqMessage.GetStringProperty("nsbempty"));
        }
    }
}
