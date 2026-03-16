#nullable enable

namespace NServiceBus.Transport.IBMMQ.Tests;

using System.Collections;
using IBM.WMQ;
using NUnit.Framework;

[TestFixture]
[Order(1)]
public class MqMessageClearBehaviorTests
{
    static readonly Hashtable ConnectionProperties = new()
    {
        { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_MANAGED },
        { MQC.HOST_NAME_PROPERTY, TestConnectionDetails.Host },
        { MQC.PORT_PROPERTY, TestConnectionDetails.Port },
        { MQC.CHANNEL_PROPERTY, TestConnectionDetails.Channel },
        { MQC.USER_ID_PROPERTY, TestConnectionDetails.User },
        { MQC.PASSWORD_PROPERTY, TestConnectionDetails.Password }
    };

    const string QueueName = "SYSTEM.DEFAULT.LOCAL.QUEUE";

    static MQQueueManager Connect() =>
        new(TestConnectionDetails.QueueManagerName, ConnectionProperties);

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

    [Test]
    [Order(1)]
    public void ClearMessage_does_not_clear_named_properties_on_put()
    {
        using var qm = Connect();
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
    public void ClearMessage_properties_are_replaced_by_get()
    {
        using var qm = Connect();
        using var outputQueue = qm.AccessQueue(QueueName, MQC.MQOO_OUTPUT);
        var pmo = new MQPutMessageOptions();

        // Send message A with PropA
        var msgA = new MQMessage();
        var pdA = CreatePropertyDescriptor();
        msgA.SetStringProperty("PropA", pdA, "a");
        msgA.WriteString("messageA");
        outputQueue.Put(msgA, pmo);
        var messageIdA = (byte[])msgA.MessageId.Clone();

        // Send message B with PropB only (no PropA)
        var msgB = new MQMessage();
        var pdB = CreatePropertyDescriptor();
        msgB.SetStringProperty("PropB", pdB, "b");
        msgB.WriteString("messageB");
        outputQueue.Put(msgB, pmo);
        var messageIdB = (byte[])msgB.MessageId.Clone();

        // Read messages using a single reused MQMessage
        using var inputQueue = qm.AccessQueue(QueueName, MQC.MQOO_INPUT_SHARED);
        var gmo = new MQGetMessageOptions { Options = MQC.MQGMO_NO_WAIT };

        var reused = new MQMessage { MessageId = messageIdA };

        // Get message A
        gmo.MatchOptions = MQC.MQMO_MATCH_MSG_ID;
        inputQueue.Get(reused, gmo);

        var propAFromA = TryGetStringProperty(reused, "PropA");
        Assert.That(propAFromA, Is.EqualTo("a"), "Message A should have PropA=a");

        // ClearMessage and reset identifiers before reuse
        reused.ClearMessage();
        reused.MessageId = MQC.MQMI_NONE;
        reused.CorrelationId = MQC.MQCI_NONE;

        // Get message B
        reused.MessageId = messageIdB;
        gmo.MatchOptions = MQC.MQMO_MATCH_MSG_ID;
        inputQueue.Get(reused, gmo);

        var propBFromB = TryGetStringProperty(reused, "PropB");
        Assert.That(propBFromB, Is.EqualTo("b"), "Message B should have PropB=b");

        // Key assertion: does Get() replace all properties, or does PropA leak through?
        var propAFromB = TryGetStringProperty(reused, "PropA");

        // If Get() fully replaces properties, PropA should be null
        // If ClearMessage didn't clear and Get doesn't replace, PropA will still be "a"
        TestContext.Out.WriteLine($"PropA after getting message B: {propAFromB ?? "(null)"}");
        TestContext.Out.WriteLine($"PropB after getting message B: {propBFromB ?? "(null)"}");

        Assert.That(propAFromB, Is.Null,
            "Get() should replace all properties — PropA should not be present after getting message B");
    }
}
