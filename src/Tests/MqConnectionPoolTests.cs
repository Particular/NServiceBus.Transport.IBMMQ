namespace NServiceBus.Transport.IBMMQ.Tests;

using System.Collections;
using System.Threading.Tasks;
using IBM.WMQ;
using NUnit.Framework;

[TestFixture]
public class MqConnectionPoolTests
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

    [Test]
    public async Task Discard_allows_new_connection_to_be_created()
    {
        var pool = new MqConnectionPool(
            () => new MqConnection(
                NServiceBus.Logging.LogManager.GetLogger<MqConnection>(),
                new MQQueueManager(TestConnectionDetails.QueueManagerName, ConnectionProperties),
                name => name,
                (_, _) => { },
                100),
            maxSize: 1);

        var conn1 = pool.Rent();
        pool.Discard(conn1);

        var conn2 = pool.Rent();
        Assert.That(conn2, Is.Not.SameAs(conn1));

        pool.Return(conn2);
        await pool.DisposeAsync().ConfigureAwait(false);
    }
}
