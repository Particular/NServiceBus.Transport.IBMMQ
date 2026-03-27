#pragma warning disable PS0024 // "I" in IBMMQ is from IBM, not an interface prefix
namespace NServiceBus.Transport.IBMMQ.Tests;

using System;
using System.Collections.Generic;
using IBM.WMQ;
using NServiceBus.Performance.TimeToBeReceived;
using NServiceBus.Transport;
using NUnit.Framework;

[TestFixture]
public class IBMMQMessageConverterTests
{
    static readonly IBMMQMessageConverter converter = new(new MqPropertyNameEncoder(), MQC.CODESET_UTF);
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

            var mqMessage = converter.ToNative(operation);

            Assert.That(mqMessage.Expiry, Is.EqualTo(300)); // 30s * 10 = 300 tenths-of-seconds
        }

        [Test]
        public void Sets_unlimited_expiry_when_DiscardIfNotReceivedBefore_is_null()
        {
            var operation = CreateOperation(properties: []);

            var mqMessage = converter.ToNative(operation);

            Assert.That(mqMessage.Expiry, Is.EqualTo(MQC.MQEI_UNLIMITED));
        }

        [Test]
        public void Sets_unlimited_expiry_when_properties_are_empty()
        {
            var operation = CreateOperation();

            var mqMessage = converter.ToNative(operation);

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

            var mqMessage = converter.ToNative(operation);

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

            var mqMessage = converter.ToNative(operation);

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

            var mqMessage = converter.ToNative(operation);

            var expected = new byte[24];
            Array.Copy(correlationId.ToByteArray(), expected, 16);
            Assert.That(mqMessage.CorrelationId, Is.EqualTo(expected));
        }

        [Test]
        public void Sets_MessageId_from_hex_string()
        {
            var nativeId = new byte[24];
            Array.Copy(Guid.NewGuid().ToByteArray(), nativeId, 16);
            var hexId = Convert.ToHexString(nativeId);
            var headers = new Dictionary<string, string>
            {
                { Headers.MessageId, hexId }
            };
            var operation = CreateOperation(headers: headers);

            var mqMessage = converter.ToNative(operation);

            Assert.That(mqMessage.MessageId, Is.EqualTo(nativeId));
        }

        [Test]
        public void Sets_CorrelationId_from_hex_string()
        {
            var nativeCorrel = new byte[24];
            Array.Copy(Guid.NewGuid().ToByteArray(), nativeCorrel, 16);
            var hexCorrel = Convert.ToHexString(nativeCorrel);
            var headers = new Dictionary<string, string>
            {
                { Headers.CorrelationId, hexCorrel }
            };
            var operation = CreateOperation(headers: headers);

            var mqMessage = converter.ToNative(operation);

            Assert.That(mqMessage.CorrelationId, Is.EqualTo(nativeCorrel));
        }

        [Test]
        public void Non_guid_non_hex_MessageId_does_not_set_native_field()
        {
            var headers = new Dictionary<string, string>
            {
                { Headers.MessageId, "not-a-guid-or-hex" }
            };
            var operation = CreateOperation(headers: headers);

            var mqMessage = converter.ToNative(operation);

            Assert.That(mqMessage.MessageId, Is.EqualTo(new byte[24]));
        }

        [Test]
        public void Native_MessageId_round_trips_through_hex()
        {
            // Simulate receiving a native message, then forwarding it
            var originalId = new byte[24];
            new Random(42).NextBytes(originalId);

            var incoming = new MQMessage { MessageId = originalId };
            incoming.Write(System.Text.Encoding.UTF8.GetBytes("test"));
            incoming.Seek(0);

            var receivedHeaders = new Dictionary<string, string>();
            string messageId = string.Empty;
            converter.FromNative(incoming, receivedHeaders, ref messageId);

            // Forward: use the lifted header to set the native field
            var operation = CreateOperation(headers: receivedHeaders);
            var outgoing = converter.ToNative(operation);

            Assert.That(outgoing.MessageId, Is.EqualTo(originalId));
        }

        [Test]
        public void Sets_ReplyToQueueName_from_header()
        {
            var headers = new Dictionary<string, string>
            {
                { Headers.ReplyToAddress, "my.reply.queue" }
            };
            var operation = CreateOperation(headers: headers);

            var mqMessage = converter.ToNative(operation);

            Assert.That(mqMessage.ReplyToQueueName, Is.EqualTo("my.reply.queue"));
        }

        [Test]
        public void Writes_message_body()
        {
            var body = System.Text.Encoding.UTF8.GetBytes("hello world");
            var operation = CreateOperation(body: body);

            var mqMessage = converter.ToNative(operation);

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
            var mqMessage = converter.ToNative(operation);
            Assert.That(mqMessage.MessageType, Is.EqualTo(MQC.MQMT_DATAGRAM));
        }

        [Test]
        public void Persistence_is_persistent()
        {
            var operation = CreateOperation();
            var mqMessage = converter.ToNative(operation);
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
            var mqMessage = converter.ToNative(operation);
            Assert.That(mqMessage.Persistence, Is.EqualTo(MQC.MQPER_NOT_PERSISTENT));
        }

        [Test]
        public void CharacterSet_uses_configured_value()
        {
            var customConverter = new IBMMQMessageConverter(new MqPropertyNameEncoder(), 819);
            var operation = CreateOperation();
            var mqMessage = customConverter.ToNative(operation);
            Assert.That(mqMessage.CharacterSet, Is.EqualTo(819));
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

            var mqMessage = converter.ToNative(operation);

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

            var mqMessage = converter.ToNative(operation);

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

            var mqMessage = converter.ToNative(operation);

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

            var mqMessage = converter.ToNative(operation);

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

            var mqMessage = converter.ToNative(operation);

            Assert.Throws<MQException>(() => mqMessage.GetStringProperty("nsbempty"));
        }
    }

    [TestFixture]
    public class RoundTrip
    {
        [Test]
        public void Headers_survive_ToNative_then_FromNative()
        {
            var headers = new Dictionary<string, string>
            {
                { Headers.MessageId, Guid.NewGuid().ToString() },
                { "NServiceBus.EnclosedMessageTypes", "MyApp.Events.OrderPlaced" },
                { "custom-header", "custom-value" }
            };
            var body = System.Text.Encoding.UTF8.GetBytes("payload");
            var operation = CreateOperation(headers: headers, body: body);

            var mqMessage = converter.ToNative(operation);
            mqMessage.Seek(0);

            var receivedHeaders = new Dictionary<string, string>();
            string messageId = string.Empty;
            var receivedBody = converter.FromNative(mqMessage, receivedHeaders, ref messageId);

            Assert.That(receivedHeaders["NServiceBus.EnclosedMessageTypes"], Is.EqualTo("MyApp.Events.OrderPlaced"));
            Assert.That(receivedHeaders["custom-header"], Is.EqualTo("custom-value"));
            Assert.That(receivedBody, Is.EqualTo(body));
        }

        [Test]
        public void Empty_header_values_survive_round_trip()
        {
            var headers = new Dictionary<string, string>
            {
                { Headers.MessageId, Guid.NewGuid().ToString() },
                { "EmptyHeader", "" },
                { "NormalHeader", "value" }
            };
            var operation = CreateOperation(headers: headers);

            var mqMessage = converter.ToNative(operation);
            mqMessage.Seek(0);

            var receivedHeaders = new Dictionary<string, string>();
            string messageId = string.Empty;
            converter.FromNative(mqMessage, receivedHeaders, ref messageId);

            Assert.That(receivedHeaders["EmptyHeader"], Is.EqualTo(""));
            Assert.That(receivedHeaders["NormalHeader"], Is.EqualTo("value"));
        }

        [Test]
        public void Empty_body_survives_round_trip()
        {
            var headers = new Dictionary<string, string>
            {
                { Headers.MessageId, Guid.NewGuid().ToString() }
            };
            var operation = CreateOperation(headers: headers, body: []);

            var mqMessage = converter.ToNative(operation);
            mqMessage.Seek(0);

            var receivedHeaders = new Dictionary<string, string>();
            string messageId = string.Empty;
            var receivedBody = converter.FromNative(mqMessage, receivedHeaders, ref messageId);

            Assert.That(receivedBody, Is.Empty);
        }

        [Test]
        public void MessageId_falls_back_to_native_when_header_is_empty()
        {
            var headers = new Dictionary<string, string>
            {
                { Headers.MessageId, "" }
            };
            var operation = CreateOperation(headers: headers);
            var mqMessage = converter.ToNative(operation);

            var nativeId = new byte[24];
            Array.Copy(Guid.NewGuid().ToByteArray(), nativeId, 16);
            mqMessage.MessageId = nativeId;

            mqMessage.Seek(0);
            var receivedHeaders = new Dictionary<string, string>();
            string messageId = string.Empty;
            converter.FromNative(mqMessage, receivedHeaders, ref messageId);

            Assert.That(messageId, Is.EqualTo(Convert.ToHexString(nativeId)));
        }
    }

    [TestFixture]
    public class NativePropertyLifting
    {
        [Test]
        public void Lifts_CorrelationId_when_no_header_exists()
        {
            var correlationGuid = Guid.NewGuid();
            var headers = new Dictionary<string, string>
            {
                { Headers.MessageId, Guid.NewGuid().ToString() }
            };
            var operation = CreateOperation(headers: headers);
            var mqMessage = converter.ToNative(operation);

            var correlBytes = new byte[24];
            Array.Copy(correlationGuid.ToByteArray(), correlBytes, 16);
            mqMessage.CorrelationId = correlBytes;

            mqMessage.Seek(0);
            var receivedHeaders = new Dictionary<string, string>();
            string messageId = string.Empty;
            converter.FromNative(mqMessage, receivedHeaders, ref messageId);

            Assert.That(receivedHeaders, Does.ContainKey(Headers.CorrelationId));
            Assert.That(receivedHeaders[Headers.CorrelationId], Is.EqualTo(Convert.ToHexString(correlBytes)));
        }

        [Test]
        public void Does_not_overwrite_existing_CorrelationId_header()
        {
            var headerCorrelationId = Guid.NewGuid().ToString();
            var headers = new Dictionary<string, string>
            {
                { Headers.MessageId, Guid.NewGuid().ToString() },
                { Headers.CorrelationId, headerCorrelationId }
            };
            var operation = CreateOperation(headers: headers);
            var mqMessage = converter.ToNative(operation);

            var differentCorrel = new byte[24];
            Array.Copy(Guid.NewGuid().ToByteArray(), differentCorrel, 16);
            mqMessage.CorrelationId = differentCorrel;

            mqMessage.Seek(0);
            var receivedHeaders = new Dictionary<string, string>();
            string messageId = string.Empty;
            converter.FromNative(mqMessage, receivedHeaders, ref messageId);

            Assert.That(receivedHeaders[Headers.CorrelationId], Is.EqualTo(headerCorrelationId));
        }

        [Test]
        public void Does_not_lift_CorrelationId_when_all_zeros()
        {
            var headers = new Dictionary<string, string>
            {
                { Headers.MessageId, Guid.NewGuid().ToString() }
            };
            var operation = CreateOperation(headers: headers);
            var mqMessage = converter.ToNative(operation);

            mqMessage.CorrelationId = new byte[24];

            mqMessage.Seek(0);
            var receivedHeaders = new Dictionary<string, string>();
            string messageId = string.Empty;
            converter.FromNative(mqMessage, receivedHeaders, ref messageId);

            Assert.That(receivedHeaders, Does.Not.ContainKey(Headers.CorrelationId));
        }

        [Test]
        public void Lifts_ReplyToQueueName_when_no_header_exists()
        {
            var headers = new Dictionary<string, string>
            {
                { Headers.MessageId, Guid.NewGuid().ToString() }
            };
            var operation = CreateOperation(headers: headers);
            var mqMessage = converter.ToNative(operation);

            mqMessage.ReplyToQueueName = "REPLY.QUEUE";

            mqMessage.Seek(0);
            var receivedHeaders = new Dictionary<string, string>();
            string messageId = string.Empty;
            converter.FromNative(mqMessage, receivedHeaders, ref messageId);

            Assert.That(receivedHeaders, Does.ContainKey(Headers.ReplyToAddress));
            Assert.That(receivedHeaders[Headers.ReplyToAddress], Is.EqualTo("REPLY.QUEUE"));
        }

        [Test]
        public void Does_not_overwrite_existing_ReplyToAddress_header()
        {
            var headers = new Dictionary<string, string>
            {
                { Headers.MessageId, Guid.NewGuid().ToString() },
                { Headers.ReplyToAddress, "original.reply.queue" }
            };
            var operation = CreateOperation(headers: headers);
            var mqMessage = converter.ToNative(operation);

            // Set a different native ReplyToQueueName
            mqMessage.ReplyToQueueName = "DIFFERENT.QUEUE";

            mqMessage.Seek(0);
            var receivedHeaders = new Dictionary<string, string>();
            string messageId = string.Empty;
            converter.FromNative(mqMessage, receivedHeaders, ref messageId);

            Assert.That(receivedHeaders[Headers.ReplyToAddress], Is.EqualTo("original.reply.queue"));
        }

        [Test]
        public void Lifts_NonDurableMessage_when_persistence_is_non_persistent()
        {
            var headers = new Dictionary<string, string>
            {
                { Headers.MessageId, Guid.NewGuid().ToString() }
            };
            var operation = CreateOperation(headers: headers);
            var mqMessage = converter.ToNative(operation);

            mqMessage.Persistence = MQC.MQPER_NOT_PERSISTENT;

            mqMessage.Seek(0);
            var receivedHeaders = new Dictionary<string, string>();
            string messageId = string.Empty;
            converter.FromNative(mqMessage, receivedHeaders, ref messageId);

            Assert.That(receivedHeaders, Does.ContainKey(Headers.NonDurableMessage));
        }

        [Test]
        public void Does_not_lift_NonDurableMessage_when_persistent()
        {
            var headers = new Dictionary<string, string>
            {
                { Headers.MessageId, Guid.NewGuid().ToString() }
            };
            var operation = CreateOperation(headers: headers);
            var mqMessage = converter.ToNative(operation);

            mqMessage.Persistence = MQC.MQPER_PERSISTENT;

            mqMessage.Seek(0);
            var receivedHeaders = new Dictionary<string, string>();
            string messageId = string.Empty;
            converter.FromNative(mqMessage, receivedHeaders, ref messageId);

            Assert.That(receivedHeaders, Does.Not.ContainKey(Headers.NonDurableMessage));
        }

        [Test]
        public void Lifts_TimeToBeReceived_from_expiry()
        {
            var headers = new Dictionary<string, string>
            {
                { Headers.MessageId, Guid.NewGuid().ToString() }
            };
            var operation = CreateOperation(headers: headers);
            var mqMessage = converter.ToNative(operation);

            mqMessage.Expiry = 300; // 30 seconds in tenths

            mqMessage.Seek(0);
            var receivedHeaders = new Dictionary<string, string>();
            string messageId = string.Empty;
            converter.FromNative(mqMessage, receivedHeaders, ref messageId);

            Assert.That(receivedHeaders, Does.ContainKey(Headers.TimeToBeReceived));
            Assert.That(receivedHeaders[Headers.TimeToBeReceived], Is.EqualTo(TimeSpan.FromSeconds(30).ToString()));
        }

        [Test]
        public void Does_not_lift_TimeToBeReceived_when_unlimited()
        {
            var headers = new Dictionary<string, string>
            {
                { Headers.MessageId, Guid.NewGuid().ToString() }
            };
            var operation = CreateOperation(headers: headers);
            var mqMessage = converter.ToNative(operation);

            mqMessage.Expiry = MQC.MQEI_UNLIMITED;

            mqMessage.Seek(0);
            var receivedHeaders = new Dictionary<string, string>();
            string messageId = string.Empty;
            converter.FromNative(mqMessage, receivedHeaders, ref messageId);

            Assert.That(receivedHeaders, Does.Not.ContainKey(Headers.TimeToBeReceived));
        }

        [Test]
        public void Does_not_overwrite_existing_TimeToBeReceived_header()
        {
            var headers = new Dictionary<string, string>
            {
                { Headers.MessageId, Guid.NewGuid().ToString() },
                { Headers.TimeToBeReceived, TimeSpan.FromMinutes(5).ToString() }
            };
            var operation = CreateOperation(headers: headers);
            var mqMessage = converter.ToNative(operation);

            mqMessage.Expiry = 300; // different value

            mqMessage.Seek(0);
            var receivedHeaders = new Dictionary<string, string>();
            string messageId = string.Empty;
            converter.FromNative(mqMessage, receivedHeaders, ref messageId);

            Assert.That(receivedHeaders[Headers.TimeToBeReceived], Is.EqualTo(TimeSpan.FromMinutes(5).ToString()));
        }

        [Test]
        public void Lifts_MessageId_from_native_when_no_header_exists()
        {
            // Create a raw MQMessage (no NServiceBus headers)
            var mqMessage = new MQMessage();
            var nativeId = new byte[24];
            Array.Copy(Guid.NewGuid().ToByteArray(), nativeId, 16);
            mqMessage.MessageId = nativeId;
            mqMessage.Write(System.Text.Encoding.UTF8.GetBytes("test"));

            mqMessage.Seek(0);
            var receivedHeaders = new Dictionary<string, string>();
            string messageId = string.Empty;
            converter.FromNative(mqMessage, receivedHeaders, ref messageId);

            Assert.That(receivedHeaders, Does.ContainKey(Headers.MessageId));
            Assert.That(receivedHeaders[Headers.MessageId], Is.EqualTo(Convert.ToHexString(nativeId)));
            Assert.That(messageId, Is.EqualTo(Convert.ToHexString(nativeId)));
        }
    }
}
