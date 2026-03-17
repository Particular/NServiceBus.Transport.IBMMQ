namespace NServiceBus.Transport.IBMMQ.Tests;

using System.Collections;
using IBM.WMQ;
using NUnit.Framework;

[TestFixture]
public class MqConnectionTests
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

    static MQQueueManager Connect() =>
        new(TestConnectionDetails.QueueManagerName, ConnectionProperties);

    [Test]
    public void Each_connection_has_independent_queue_handle_cache()
    {
        using var connA = new MqConnection(
            NServiceBus.Logging.LogManager.GetLogger<MqConnection>(),
            Connect(),
            name => name,
            (_, _) => { },
            100);
        using var connB = new MqConnection(
            NServiceBus.Logging.LogManager.GetLogger<MqConnection>(),
            Connect(),
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
            Connect(),
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