#nullable enable

namespace NServiceBus.Transport.IBMMQ.Tests;

using System;
using IBM.WMQ;
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
        // an MQMessage across multiple receives. It documents that:
        // 1. ClearMessage does NOT reset MessageId/CorrelationId — explicit reset is required
        // 2. Get() DOES replace named properties — stale properties from previous message don't leak
        // Without explicit identifier reset, the default MatchOptions
        // (MQMO_MATCH_MSG_ID | MQMO_MATCH_CORREL_ID) would match the previous message's identifiers.
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

        // ClearMessage resets body but NOT identifiers
        reused.ClearMessage();

        Assert.That(reused.MessageId, Is.Not.EqualTo(MQC.MQMI_NONE),
            "ClearMessage should NOT reset MessageId — explicit reset to MQMI_NONE is required");

        // Explicit reset required before next Get
        reused.MessageId = MQC.MQMI_NONE;
        reused.CorrelationId = MQC.MQCI_NONE;

        // Get message B
        reused.MessageId = messageIdB;
        inputQueue.Get(reused, gmo);

        // Get() replaces identifiers with message B's values
        Assert.That(reused.MessageId, Is.EqualTo(messageIdB),
            "Get should replace MessageId with message B's identifier");

        // Get() replaces named properties — PropA from message A should not leak through
        Assert.That(TryGetStringProperty(reused, "PropB"), Is.EqualTo("b"),
            "Message B should have PropB=b");
        Assert.That(TryGetStringProperty(reused, "PropA"), Is.Null,
            "Get() should replace all properties — PropA from message A should not be present");
    }
}
