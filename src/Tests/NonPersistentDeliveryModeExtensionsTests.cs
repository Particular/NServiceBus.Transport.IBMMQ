#pragma warning disable PS0024 // "I" in IBMMQ is from IBM, not an interface prefix
namespace NServiceBus.Transport.IBMMQ.Tests;

using NUnit.Framework;

[TestFixture]
public class NonPersistentDeliveryModeExtensionsTests
{
    [Test]
    public void SendOptions_sets_NonDurableMessage_header()
    {
        var options = new SendOptions();

        options.UseNonPersistentDeliveryMode();

        Assert.That(options.GetHeaders()[Headers.NonDurableMessage], Is.EqualTo(bool.TrueString));
    }

    [Test]
    public void PublishOptions_sets_NonDurableMessage_header()
    {
        var options = new PublishOptions();

        options.UseNonPersistentDeliveryMode();

        Assert.That(options.GetHeaders()[Headers.NonDurableMessage], Is.EqualTo(bool.TrueString));
    }

    [Test]
    public void ReplyOptions_sets_NonDurableMessage_header()
    {
        var options = new ReplyOptions();

        options.UseNonPersistentDeliveryMode();

        Assert.That(options.GetHeaders()[Headers.NonDurableMessage], Is.EqualTo(bool.TrueString));
    }

    [Test]
    public void SendOptions_throws_on_null()
    {
        SendOptions options = null;

        Assert.Throws<ArgumentNullException>(() => options.UseNonPersistentDeliveryMode());
    }

    [Test]
    public void PublishOptions_throws_on_null()
    {
        PublishOptions options = null;

        Assert.Throws<ArgumentNullException>(() => options.UseNonPersistentDeliveryMode());
    }

    [Test]
    public void ReplyOptions_throws_on_null()
    {
        ReplyOptions options = null;

        Assert.Throws<ArgumentNullException>(() => options.UseNonPersistentDeliveryMode());
    }
}
