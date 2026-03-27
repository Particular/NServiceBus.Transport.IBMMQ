namespace NServiceBus.Transport.IBMMQ.Tests;

using NUnit.Framework;

[TestFixture]
public class MqPropertyNameEncoderTests
{
    readonly MqPropertyNameEncoder encoder = new();

    [TestCase("SimpleHeader", "SimpleHeader")]
    [TestCase("NServiceBus.MessageId", "NServiceBus_x002EMessageId")]
    [TestCase("my-header", "my_x002Dheader")]
    [TestCase("my_header", "my__header")]
    [TestCase("$.diagnostics", "_x0024_x002Ediagnostics")]
    [TestCase("has space", "has_x0020space")]
    [TestCase("a.b-c_d", "a_x002Eb_x002Dc__d")]
    [TestCase("", "")]
    [TestCase("_", "__")]
    [TestCase("__", "____")]
    [TestCase("a\0b", "a_x0000b")]
    [TestCase("tab\there", "tab_x0009here")]
    [TestCase("_x0041", "__x0041")]
    public void Encode(string input, string expected)
    {
        Assert.That(encoder.Encode(input), Is.EqualTo(expected));
    }

    [TestCase("SimpleHeader", "SimpleHeader")]
    [TestCase("NServiceBus_x002EMessageId", "NServiceBus.MessageId")]
    [TestCase("my__header", "my_header")]
    [TestCase("a_x002Eb_x002Dc__d", "a.b-c_d")]
    [TestCase("", "")]
    [TestCase("a_b", "a_b")]
    [TestCase("__", "_")]
    [TestCase("____", "__")]
    [TestCase("_x0000", "\0")]
    [TestCase("trailing_", "trailing_")]
    [TestCase("_xZZZZ", "_xZZZZ")]
    [TestCase("_x00", "_x00")]
    public void Decode(string input, string expected)
    {
        Assert.That(encoder.Decode(input), Is.EqualTo(expected));
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
        var encoded = encoder.Encode(original);
        var decoded = encoder.Decode(encoded);
        Assert.That(decoded, Is.EqualTo(original));
    }

    [Test]
    public void Encode_caches_results()
    {
        var result1 = encoder.Encode("NServiceBus.MessageId");
        var result2 = encoder.Encode("NServiceBus.MessageId");
        Assert.That(result1, Is.SameAs(result2));
    }

    [Test]
    public void Decode_caches_results()
    {
        var result1 = encoder.Decode("NServiceBus_x002EMessageId");
        var result2 = encoder.Decode("NServiceBus_x002EMessageId");
        Assert.That(result1, Is.SameAs(result2));
    }
}
