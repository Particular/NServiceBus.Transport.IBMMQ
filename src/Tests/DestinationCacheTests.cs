namespace NServiceBus.Transport.IBMMQ.Tests;

using IBM.WMQ;
using NServiceBus.Logging;
using NUnit.Framework;

[TestFixture]
[Category("RequiresBroker")]
public class DestinationCacheTests
{
    const string QueueName = "SYSTEM.DEFAULT.LOCAL.QUEUE";

    [OneTimeSetUp]
    public void Setup() => BrokerRequirement.Verify();

    [Test]
    public void GetOrAdd_returns_same_handle_for_same_key()
    {
        using var qm = TestBrokerConnection.Connect();
        using var cache = new DestinationCache<MQQueue>(
            LogManager.GetLogger<DestinationCacheTests>(),
            capacity: 10);

        var handle1 = cache.GetOrAdd(QueueName, _ => qm.AccessQueue(QueueName, MQC.MQOO_INQUIRE));
        var handle2 = cache.GetOrAdd(QueueName, _ => qm.AccessQueue(QueueName, MQC.MQOO_INQUIRE));

        Assert.That(handle2, Is.SameAs(handle1));
    }

    [Test]
    public void GetOrAdd_evicts_lru_when_capacity_exceeded()
    {
        using var qm = TestBrokerConnection.Connect();
        var openCount = 0;
        using var cache = new DestinationCache<MQQueue>(
            LogManager.GetLogger<DestinationCacheTests>(),
            capacity: 2);

        MQQueue Open(string _)
        {
            openCount++;
            return qm.AccessQueue(QueueName, MQC.MQOO_INQUIRE);
        }

        cache.GetOrAdd("a", Open);
        cache.GetOrAdd("b", Open);
        cache.GetOrAdd("c", Open); // evicts "a"

        Assert.That(openCount, Is.EqualTo(3));

        // "a" was evicted — factory should be called again
        cache.GetOrAdd("a", Open);
        Assert.That(openCount, Is.EqualTo(4));
    }

    [Test]
    public void GetOrAdd_promotes_recently_accessed_entry()
    {
        using var qm = TestBrokerConnection.Connect();
        var openCount = 0;
        using var cache = new DestinationCache<MQQueue>(
            LogManager.GetLogger<DestinationCacheTests>(),
            capacity: 2);

        MQQueue Open(string _)
        {
            openCount++;
            return qm.AccessQueue(QueueName, MQC.MQOO_INQUIRE);
        }

        cache.GetOrAdd("a", Open); // [a]
        cache.GetOrAdd("b", Open); // [b, a]
        cache.GetOrAdd("a", Open); // promotes a → [a, b]
        cache.GetOrAdd("c", Open); // evicts "b" (LRU) → [c, a]

        // "a" should still be cached
        cache.GetOrAdd("a", Open);
        Assert.That(openCount, Is.EqualTo(3)); // a, b, c — no re-open of a

        // "b" was evicted
        cache.GetOrAdd("b", Open);
        Assert.That(openCount, Is.EqualTo(4));
    }

    [Test]
    public void Evict_removes_entry()
    {
        using var qm = TestBrokerConnection.Connect();
        var openCount = 0;
        using var cache = new DestinationCache<MQQueue>(
            LogManager.GetLogger<DestinationCacheTests>(),
            capacity: 10);

        MQQueue Open(string _)
        {
            openCount++;
            return qm.AccessQueue(QueueName, MQC.MQOO_INQUIRE);
        }

        cache.GetOrAdd("a", Open);
        cache.Evict("a");

        cache.GetOrAdd("a", Open);
        Assert.That(openCount, Is.EqualTo(2));
    }

    [Test]
    public void Evict_is_idempotent_for_unknown_key()
    {
        using var cache = new DestinationCache<MQQueue>(
            LogManager.GetLogger<DestinationCacheTests>(),
            capacity: 10);

        Assert.DoesNotThrow(() => cache.Evict("nonexistent"));
    }

    [Test]
    public void Dispose_is_idempotent()
    {
        var cache = new DestinationCache<MQQueue>(
            LogManager.GetLogger<DestinationCacheTests>(),
            capacity: 10);

        cache.Dispose();
        Assert.DoesNotThrow(() => cache.Dispose());
    }

    [Test]
    public void GetOrAdd_throws_after_dispose()
    {
        var cache = new DestinationCache<MQQueue>(
            LogManager.GetLogger<DestinationCacheTests>(),
            capacity: 10);

        cache.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            cache.GetOrAdd("a", _ => throw new InvalidOperationException("should not be called")));
    }
}
