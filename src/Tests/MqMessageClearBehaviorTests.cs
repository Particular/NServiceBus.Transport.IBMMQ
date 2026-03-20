#nullable enable

namespace NServiceBus.Transport.IBMMQ.Tests;

using System;
using System.Collections.Generic;
using IBM.WMQ;
using NServiceBus.Transport;
using NUnit.Framework;

[TestFixture]
[Order(1)]
[Category("RequiresBroker")]
public class MqMessageClearBehaviorTests
{
    const string QueueName = "SYSTEM.DEFAULT.LOCAL.QUEUE";

    static MQPropertyDescriptor CreatePropertyDescriptor() =>
        new() { Support = MQC.MQPD_SUPPORT_OPTIONAL };

    static string? TryGetStringProperty(MQMessage message, string name)
    {
        try
        {
            var pd = CreatePropertyDescriptor();
            return message.GetStringProperty(name, pd);
        }
        catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_PROPERTY_NOT_AVAILABLE)
        {
            return null;
        }
    }

    [OneTimeSetUp]
    public void Setup() => BrokerRequirement.Verify();

    [Test]
    [Order(1)]
    public void ClearMessage_does_not_clear_named_properties_on_put()
    {
        using var qm = TestBrokerConnection.Connect();
        using var outputQueue = qm.AccessQueue(QueueName, MQC.MQOO_OUTPUT);
        var pmo = new MQPutMessageOptions();

        var message = new MQMessage();
        var pd = CreatePropertyDescriptor();

        // First message: set TestProp
        message.SetStringProperty("TestProp", pd, "value1");
        message.WriteString("message1");
        outputQueue.Put(message, pmo);
        var firstMessageId = (byte[])message.MessageId.Clone();

        // ClearMessage and reset identifiers
        message.ClearMessage();
        message.MessageId = MQC.MQMI_NONE;
        message.CorrelationId = MQC.MQCI_NONE;

        // Second message: set only TestProp2, do NOT re-set TestProp
        pd = CreatePropertyDescriptor();
        message.SetStringProperty("TestProp2", pd, "value2");
        message.WriteString("message2");
        outputQueue.Put(message, pmo);
        var secondMessageId = (byte[])message.MessageId.Clone();

        // Read both messages back
        using var inputQueue = qm.AccessQueue(QueueName, MQC.MQOO_INPUT_SHARED);
        var gmo = new MQGetMessageOptions { Options = MQC.MQGMO_NO_WAIT };

        // Read first message
        var msg1 = new MQMessage { MessageId = firstMessageId };
        gmo.MatchOptions = MQC.MQMO_MATCH_MSG_ID;
        inputQueue.Get(msg1, gmo);
        var msg1TestProp = TryGetStringProperty(msg1, "TestProp");

        // Read second message
        var msg2 = new MQMessage { MessageId = secondMessageId };
        gmo.MatchOptions = MQC.MQMO_MATCH_MSG_ID;
        inputQueue.Get(msg2, gmo);
        var msg2TestProp = TryGetStringProperty(msg2, "TestProp");
        var msg2TestProp2 = TryGetStringProperty(msg2, "TestProp2");

        // First message should have TestProp
        Assert.That(msg1TestProp, Is.EqualTo("value1"), "First message should have TestProp=value1");

        // Second message should have TestProp2
        Assert.That(msg2TestProp2, Is.EqualTo("value2"), "Second message should have TestProp2=value2");

        // Key assertion: if ClearMessage does NOT clear named properties,
        // the stale TestProp from message 1 will leak into message 2
        Assert.That(msg2TestProp, Is.EqualTo("value1"),
            "ClearMessage does NOT clear named properties — stale TestProp leaked to second message");
    }

    [Test]
    [Order(2)]
    public void Get_replaces_properties_and_identifiers_on_reused_message()
    {
        // Requires a broker: this test proves the receive (Get) behavior when reusing
        // an MQMessage across multiple receives WITHOUT PROPERTIES_IN_HANDLE.
        // 1. ClearMessage does NOT reset MessageId/CorrelationId — explicit reset is required
        // 2. Get() DOES replace named properties — stale properties from previous message don't leak
        using var qm = TestBrokerConnection.Connect();
        using var outputQueue = qm.AccessQueue(QueueName, MQC.MQOO_OUTPUT);
        var pmo = new MQPutMessageOptions();

        // Send message A with PropA
        var msgA = new MQMessage();
        msgA.SetStringProperty("PropA", CreatePropertyDescriptor(), "a");
        msgA.WriteString("messageA");
        outputQueue.Put(msgA, pmo);
        var messageIdA = (byte[])msgA.MessageId.Clone();

        // Send message B with PropB only (no PropA)
        var msgB = new MQMessage();
        msgB.SetStringProperty("PropB", CreatePropertyDescriptor(), "b");
        msgB.WriteString("messageB");
        outputQueue.Put(msgB, pmo);
        var messageIdB = (byte[])msgB.MessageId.Clone();

        // Receive both messages using a single reused MQMessage
        using var inputQueue = qm.AccessQueue(QueueName, MQC.MQOO_INPUT_SHARED);
        var gmo = new MQGetMessageOptions
        {
            Options = MQC.MQGMO_NO_WAIT,
            MatchOptions = MQC.MQMO_MATCH_MSG_ID
        };

        var reused = new MQMessage { MessageId = messageIdA };
        inputQueue.Get(reused, gmo);

        Assert.That(TryGetStringProperty(reused, "PropA"), Is.EqualTo("a"),
            "Message A should have PropA=a");

        reused.ClearMessage();
        reused.MessageId = messageIdB;
        reused.CorrelationId = MQC.MQCI_NONE;
        inputQueue.Get(reused, gmo);

        Assert.That(TryGetStringProperty(reused, "PropB"), Is.EqualTo("b"),
            "Message B should have PropB=b");
        Assert.That(TryGetStringProperty(reused, "PropA"), Is.Null,
            "Get() without PROPERTIES_IN_HANDLE replaces properties — PropA should not leak");
    }

    [Test]
    [Order(3)]
    public void Get_with_PROPERTIES_IN_HANDLE_leaks_stale_properties_on_reused_message()
    {
        // Demonstrates that MQGMO_PROPERTIES_IN_HANDLE causes stale properties to leak
        // when reusing an MQMessage across receives. ClearMessage() does not reset the
        // property handle, so properties from message A appear on message B.
        // This is why the receive strategy must allocate a new MQMessage per receive.
        using var qm = TestBrokerConnection.Connect();
        using var outputQueue = qm.AccessQueue(QueueName, MQC.MQOO_OUTPUT);
        var pmo = new MQPutMessageOptions();

        // Send message A with PropA
        var msgA = new MQMessage();
        msgA.SetStringProperty("PropA", CreatePropertyDescriptor(), "a");
        msgA.WriteString("messageA");
        outputQueue.Put(msgA, pmo);

        // Send message B with NO properties, just a body
        var msgB = new MQMessage();
        msgB.WriteString("messageB");
        outputQueue.Put(msgB, pmo);

        // Receive both using a reused MQMessage with PROPERTIES_IN_HANDLE
        using var inputQueue = qm.AccessQueue(QueueName, MQC.MQOO_INPUT_SHARED);
        var gmo = new MQGetMessageOptions
        {
            Options = MQC.MQGMO_NO_WAIT | MQC.MQGMO_PROPERTIES_IN_HANDLE,
            MatchOptions = MQC.MQMO_NONE
        };

        var reused = new MQMessage();
        inputQueue.Get(reused, gmo);

        Assert.That(TryGetStringProperty(reused, "PropA"), Is.EqualTo("a"),
            "Message A should have PropA=a");

        // ClearMessage + reset identifiers (same as ReceiveStrategy did)
        reused.ClearMessage();
        reused.MessageId = MQC.MQMI_NONE;
        reused.CorrelationId = MQC.MQCI_NONE;

        inputQueue.Get(reused, gmo);

        var body = reused.ReadString(reused.MessageLength);
        Assert.That(body, Is.EqualTo("messageB"), "Should have received message B");

        // With PROPERTIES_IN_HANDLE, PropA from message A leaks through to message B
        var leakedPropA = TryGetStringProperty(reused, "PropA");
        Assert.That(leakedPropA, Is.EqualTo("a"),
            "PROPERTIES_IN_HANDLE causes stale properties to leak — PropA from message A " +
            "is visible on message B. This is why MQMessage must not be reused across receives.");
    }

    [Test]
    [Order(4)]
    public void Receiving_raw_message_after_NServiceBus_message_must_not_leak_headers()
    {
        // Simulates the envelope handler scenario: a NServiceBus message with headers
        // is received first, then a raw message (e.g. EBCDIC from a mainframe) with no
        // headers. The raw message must have zero headers after FromNative conversion.
        // With the old reused MQMessage approach, PROPERTIES_IN_HANDLE caused stale
        // NServiceBus headers to leak onto the raw message.
        var converter = new IBMMQMessageConverter(new MqPropertyNameEncoder());

        using var qm = TestBrokerConnection.Connect();
        using var outputQueue = qm.AccessQueue(QueueName, MQC.MQOO_OUTPUT);

        // Send a NServiceBus message (with headers) via the converter
        var nsbHeaders = new Dictionary<string, string>
        {
            { Headers.MessageId, Guid.NewGuid().ToString() },
            { Headers.EnclosedMessageTypes, "MyNamespace.MyEvent, MyAssembly" },
            { Headers.ContentType, "application/json" },
        };
        var nsbBody = System.Text.Encoding.UTF8.GetBytes("{\"Key\":\"Value\"}");
        var nsbOp = new UnicastTransportOperation(
            new OutgoingMessage(nsbHeaders[Headers.MessageId], nsbHeaders, nsbBody),
            QueueName, []);
        var nsbMqMsg = converter.ToNative(nsbOp);
        outputQueue.Put(nsbMqMsg, new MQPutMessageOptions());

        // Send a raw message (no properties, no headers — like a mainframe EBCDIC message)
        var rawMsg = new MQMessage();
        rawMsg.Write(new byte[70]); // 70-byte EBCDIC-like payload
        outputQueue.Put(rawMsg, new MQPutMessageOptions());

        // Receive both using a NEW MQMessage per receive (the fix)
        using var inputQueue = qm.AccessQueue(QueueName, MQC.MQOO_INPUT_SHARED);
        var gmo = new MQGetMessageOptions
        {
            Options = MQC.MQGMO_NO_WAIT | MQC.MQGMO_PROPERTIES_IN_HANDLE,
            MatchOptions = MQC.MQMO_NONE
        };

        // Receive message 1 (NServiceBus message)
        var recv1 = new MQMessage();
        inputQueue.Get(recv1, gmo);
        var headers1 = new Dictionary<string, string>();
        string id1 = string.Empty;
        converter.FromNative(recv1, headers1, ref id1);
        Assert.That(headers1, Is.Not.Empty, "NServiceBus message should have headers");

        // Receive message 2 (raw message) — new MQMessage prevents property handle leak
        var recv2 = new MQMessage();
        inputQueue.Get(recv2, gmo);
        var headers2 = new Dictionary<string, string>();
        string id2 = string.Empty;
        converter.FromNative(recv2, headers2, ref id2);

        Assert.That(headers2, Is.Empty,
            "Raw message received after NServiceBus message must have zero headers. " +
            "If headers leaked, the MQMessage was reused and PROPERTIES_IN_HANDLE " +
            "carried stale properties from the previous receive.");
    }
}
