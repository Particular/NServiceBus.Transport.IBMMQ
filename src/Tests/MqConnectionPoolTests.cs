namespace NServiceBus.Transport.IBMMQ.Tests;

using System.Threading.Tasks;
using IBM.WMQ;
using NUnit.Framework;

[TestFixture]
[Category("RequiresBroker")]
public class MqConnectionPoolTests
{
    [OneTimeSetUp]
    public void Setup() => BrokerRequirement.Verify();

    [Test]
    public async Task Discard_allows_new_connection_to_be_created()
    {
        var pool = new MqConnectionPool(
            NServiceBus.Logging.LogManager.GetLogger<MqConnectionPool>(),
            () => new MqConnection(
                NServiceBus.Logging.LogManager.GetLogger<MqConnection>(),
                TestBrokerConnection.Connect(),
                name => name,
                (_, _) => { },
                100),
            maxSize: 1);

        var conn1 = pool.Rent();
        pool.Discard(conn1);

        var conn2 = pool.Rent();
        Assert.That(conn2, Is.Not.SameAs(conn1));

        pool.Return(conn2);
        await pool.DisposeAsync()
            .ConfigureAwait(false);
    }
}
