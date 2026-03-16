namespace NServiceBus.Transport.IBMMQ.Tests;

using NUnit.Framework;

[TestFixture]
public class MqPropertyNameEncoderTests
{
    [TestCase("SimpleHeader", "SimpleHeader")]
    [TestCase("NServiceBus.MessageId", "NServiceBus_x002EMessageId")]
    [TestCase("my-header", "my_x002Dheader")]
    [TestCase("my_header", "my__header")]
    [TestCase("$.diagnostics", "_x0024_x002Ediagnostics")]
    [TestCase("has space", "has_x0020space")]
    [TestCase("a.b-c_d", "a_x002Eb_x002Dc__d")]
    [TestCase("", "")]
    public void Encode(string input, string expected)
    {
        Assert.That(MqPropertyNameEncoder.Encode(input), Is.EqualTo(expected));
    }

    [TestCase("SimpleHeader", "SimpleHeader")]
    [TestCase("NServiceBus_x002EMessageId", "NServiceBus.MessageId")]
    [TestCase("my__header", "my_header")]
    [TestCase("a_x002Eb_x002Dc__d", "a.b-c_d")]
    [TestCase("", "")]
    [TestCase("a_b", "a_b")]
    public void Decode(string input, string expected)
    {
        Assert.That(MqPropertyNameEncoder.Decode(input), Is.EqualTo(expected));
    }

    [TestCase("NServiceBus.MessageId")]
    [TestCase("NServiceBus.CorrelationId")]
    [TestCase("NServiceBus.ConversationId")]
    [TestCase("NServiceBus.ContentType")]
    [TestCase("NServiceBus.EnclosedMessageTypes")]
    [TestCase("NServiceBus.MessageIntent")]
    [TestCase("NServiceBus.ReplyToAddress")]
    [TestCase("NServiceBus.TimeSent")]
    [TestCase("NServiceBus.Version")]
    [TestCase("NServiceBus.NonDurableMessage")]
    [TestCase("$.diagnostics.originating.hostid")]
    [TestCase("traceparent")]
    [TestCase("SimpleHeader")]
    [TestCase("my_header")]
    [TestCase("my-header")]
    [TestCase("")]
    [TestCase("Test_xABCD")]
    [TestCase("Test__Value")]
    [TestCase("__leading")]
    [TestCase("trailing__")]
    [TestCase("a_x002E_xNotHex")]
    public void Decode_reverses_Encode(string original)
    {
        var encoded = MqPropertyNameEncoder.Encode(original);
        var decoded = MqPropertyNameEncoder.Decode(encoded);
        Assert.That(decoded, Is.EqualTo(original));
    }
}
