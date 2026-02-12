namespace NServiceBus.Transport.IbmMq.Tests;

using NServiceBus.Transport;
using NUnit.Framework;

[TestFixture]
public class AddressTranslationTests
{
    [Test]
    public void BaseAddress_only()
    {
        var address = new QueueAddress("myendpoint");

        var result = IbmMqMessageReceiver.ToTransportAddress(address);

        Assert.That(result, Is.EqualTo("myendpoint"));
    }

    [Test]
    public void BaseAddress_with_discriminator()
    {
        var address = new QueueAddress("myendpoint", discriminator: "disc1");

        var result = IbmMqMessageReceiver.ToTransportAddress(address);

        Assert.That(result, Is.EqualTo("myendpoint.disc1"));
    }

    [Test]
    public void BaseAddress_with_qualifier()
    {
        var address = new QueueAddress("myendpoint", qualifier: "qual1");

        var result = IbmMqMessageReceiver.ToTransportAddress(address);

        Assert.That(result, Is.EqualTo("myendpoint.qual1"));
    }

    [Test]
    public void BaseAddress_with_discriminator_and_qualifier()
    {
        var address = new QueueAddress("myendpoint", discriminator: "disc1", qualifier: "qual1");

        var result = IbmMqMessageReceiver.ToTransportAddress(address);

        Assert.That(result, Is.EqualTo("myendpoint.disc1.qual1"));
    }

    [Test]
    public void Null_discriminator_is_ignored()
    {
        var address = new QueueAddress("myendpoint", discriminator: null, qualifier: "qual1");

        var result = IbmMqMessageReceiver.ToTransportAddress(address);

        Assert.That(result, Is.EqualTo("myendpoint.qual1"));
    }

    [Test]
    public void Empty_discriminator_is_ignored()
    {
        var address = new QueueAddress("myendpoint", discriminator: "", qualifier: "qual1");

        var result = IbmMqMessageReceiver.ToTransportAddress(address);

        Assert.That(result, Is.EqualTo("myendpoint.qual1"));
    }
}
