namespace NServiceBus.Transport.IBMMQ.Tests;

using IBM.WMQ;
using NUnit.Framework;

[TestFixture]
[Category("RequiresBroker")]
public class MqConnectionTests
{
    [OneTimeSetUp]
    public void Setup() => BrokerRequirement.Verify();

    [Test]
    public void Each_connection_has_independent_queue_handle_cache()
    {
        using var connA = new MqConnection(
            NServiceBus.Logging.LogManager.GetLogger<MqConnection>(),
            TestBrokerConnection.Connect(),
            name => name,
            (_, _) => { },
            100);
        using var connB = new MqConnection(
            NServiceBus.Logging.LogManager.GetLogger<MqConnection>(),
            TestBrokerConnection.Connect(),
            name => name,
            (_, _) => { },
            100);

        var handleA = connA.GetOrOpenSendQueue("SYSTEM.DEFAULT.LOCAL.QUEUE");
        var handleB = connB.GetOrOpenSendQueue("SYSTEM.DEFAULT.LOCAL.QUEUE");

        Assert.That(handleB, Is.Not.SameAs(handleA));

        connA.Disconnect();
        connB.Disconnect();
    }

    [Test]
    public void Dispose_closes_cached_handles()
    {
        var connA = new MqConnection(
            NServiceBus.Logging.LogManager.GetLogger<MqConnection>(),
            TestBrokerConnection.Connect(),
            name => name,
            (_, _) => { },
            100);
        var handle = connA.GetOrOpenSendQueue("SYSTEM.DEFAULT.LOCAL.QUEUE");

        connA.Disconnect();

        var msg = new MQMessage();
        msg.WriteString("test");
        Assert.Catch<System.Exception>(() => handle.Put(msg));
    }
}
