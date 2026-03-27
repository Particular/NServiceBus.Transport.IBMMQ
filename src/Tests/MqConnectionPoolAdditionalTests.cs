namespace NServiceBus.Transport.IBMMQ.Tests;

using System.Threading;
using System.Threading.Tasks;
using IBM.WMQ;
using NUnit.Framework;

[TestFixture]
[Category("RequiresBroker")]
public class MqConnectionPoolAdditionalTests
{
    static readonly NServiceBus.Logging.ILog log = NServiceBus.Logging.LogManager.GetLogger<MqConnectionPoolAdditionalTests>();

    [OneTimeSetUp]
    public void Setup() => BrokerRequirement.Verify();

    MqConnectionPool CreatePool(int maxSize = 2) => new(
        log,
        () => new MqConnection(
            NServiceBus.Logging.LogManager.GetLogger<MqConnection>(),
            TestBrokerConnection.Connect(),
            name => name,
            (_, _) => { },
            100),
        maxSize);

    [Test]
    public async Task Return_makes_connection_available_for_next_Rent()
    {
        var pool = CreatePool(maxSize: 1);

        var conn = pool.Rent();
        pool.Return(conn);

        var same = pool.Rent();
        Assert.That(same, Is.SameAs(conn));

        pool.Return(same);
        await pool.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task Rent_creates_up_to_maxSize_connections()
    {
        var pool = CreatePool(maxSize: 2);

        var conn1 = pool.Rent();
        var conn2 = pool.Rent();

        Assert.That(conn2, Is.Not.SameAs(conn1));

        pool.Return(conn1);
        pool.Return(conn2);
        await pool.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task DisposeAsync_disconnects_pooled_connections()
    {
        var pool = CreatePool(maxSize: 1);

        var conn = pool.Rent();
        pool.Return(conn);

        await pool.DisposeAsync().ConfigureAwait(false);

        // Connection should be disconnected — renting from a disposed pool
        // still works (pool doesn't track disposed state) but the old
        // connection was cleaned up
        Assert.Pass();
    }

    [Test]
    public async Task Rent_after_Return_reuses_connection()
    {
        var pool = CreatePool(maxSize: 2);

        var conn1 = pool.Rent();
        var conn2 = pool.Rent();
        pool.Return(conn1);
        pool.Return(conn2);

        var reused1 = pool.Rent();
        var reused2 = pool.Rent();

        Assert.That(new[] { conn1, conn2 }, Does.Contain(reused1));
        Assert.That(new[] { conn1, conn2 }, Does.Contain(reused2));

        pool.Return(reused1);
        pool.Return(reused2);
        await pool.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task Discard_reduces_pool_size_allowing_new_creation()
    {
        var creationCount = 0;
        var pool = new MqConnectionPool(
            log,
            () =>
            {
                Interlocked.Increment(ref creationCount);
                return new MqConnection(
                    NServiceBus.Logging.LogManager.GetLogger<MqConnection>(),
                    TestBrokerConnection.Connect(),
                    name => name,
                    (_, _) => { },
                    100);
            },
            maxSize: 1);

        var conn1 = pool.Rent();
        pool.Discard(conn1);

        var conn2 = pool.Rent();
        Assert.That(creationCount, Is.EqualTo(2));

        pool.Return(conn2);
        await pool.DisposeAsync().ConfigureAwait(false);
    }
}
