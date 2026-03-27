namespace NServiceBus.Transport.IBMMQ.Tests;

using System;
using Microsoft.Extensions.Time.Testing;
using NServiceBus.Extensibility;
using NUnit.Framework;

[TestFixture]
public class InMemoryFailureInfoStorageTests
{
    [Test]
    public void RecordFailure_stores_entry()
    {
        var storage = new InMemoryFailureInfoStorage(TimeProvider.System);

        storage.RecordFailure("msg-1", new Exception("fail"), new ContextBag());

        Assert.That(storage.TryGetFailureInfo("msg-1", out var info), Is.True);
        Assert.That(info!.NumberOfProcessingAttempts, Is.EqualTo(1));
    }

    [Test]
    public void RecordFailure_increments_attempt_count()
    {
        var storage = new InMemoryFailureInfoStorage(TimeProvider.System);

        storage.RecordFailure("msg-1", new Exception("first"), new ContextBag());
        storage.RecordFailure("msg-1", new Exception("second"), new ContextBag());
        storage.RecordFailure("msg-1", new Exception("third"), new ContextBag());

        Assert.That(storage.TryGetFailureInfo("msg-1", out var info), Is.True);
        Assert.That(info!.NumberOfProcessingAttempts, Is.EqualTo(3));
    }

    [Test]
    public void RecordFailure_preserves_latest_exception()
    {
        var storage = new InMemoryFailureInfoStorage(TimeProvider.System);
        var latestException = new InvalidOperationException("latest");

        storage.RecordFailure("msg-1", new Exception("first"), new ContextBag());
        storage.RecordFailure("msg-1", latestException, new ContextBag());

        storage.TryGetFailureInfo("msg-1", out var info);
        Assert.That(info!.Exception, Is.SameAs(latestException));
    }

    [Test]
    public void TryGetFailureInfo_returns_false_for_unknown_message()
    {
        var storage = new InMemoryFailureInfoStorage(TimeProvider.System);

        Assert.That(storage.TryGetFailureInfo("unknown", out var info), Is.False);
        Assert.That(info, Is.Null);
    }

    [Test]
    public void ClearFailure_removes_entry()
    {
        var storage = new InMemoryFailureInfoStorage(TimeProvider.System);

        storage.RecordFailure("msg-1", new Exception("fail"), new ContextBag());
        storage.ClearFailure("msg-1");

        Assert.That(storage.TryGetFailureInfo("msg-1", out _), Is.False);
    }

    [Test]
    public void ClearFailure_is_idempotent()
    {
        var storage = new InMemoryFailureInfoStorage(TimeProvider.System);

        Assert.DoesNotThrow(() => storage.ClearFailure("nonexistent"));
    }

    [Test]
    public void Separate_messages_are_independent()
    {
        var storage = new InMemoryFailureInfoStorage(TimeProvider.System);

        storage.RecordFailure("msg-1", new Exception("a"), new ContextBag());
        storage.RecordFailure("msg-2", new Exception("b"), new ContextBag());

        storage.TryGetFailureInfo("msg-1", out var info1);
        storage.TryGetFailureInfo("msg-2", out var info2);

        Assert.That(info1!.Exception.Message, Is.EqualTo("a"));
        Assert.That(info2!.Exception.Message, Is.EqualTo("b"));
    }

    [Test]
    public void Sweep_removes_stale_entries()
    {
        var fakeTime = new FakeTimeProvider();
        var storage = new InMemoryFailureInfoStorage(fakeTime);

        storage.RecordFailure("msg-1", new Exception("fail"), new ContextBag());

        // Advance past TTL (1 min) + sweep interval (30s)
        fakeTime.Advance(TimeSpan.FromMinutes(2));

        // RecordFailure triggers sweep
        storage.RecordFailure("msg-2", new Exception("trigger"), new ContextBag());

        Assert.That(storage.TryGetFailureInfo("msg-1", out _), Is.False);
        Assert.That(storage.TryGetFailureInfo("msg-2", out _), Is.True);
    }

    [Test]
    public void Sweep_does_not_remove_fresh_entries()
    {
        var fakeTime = new FakeTimeProvider();
        var storage = new InMemoryFailureInfoStorage(fakeTime);

        storage.RecordFailure("msg-1", new Exception("fail"), new ContextBag());

        // Advance past sweep interval but not past TTL
        fakeTime.Advance(TimeSpan.FromSeconds(35));

        storage.RecordFailure("msg-2", new Exception("trigger"), new ContextBag());

        Assert.That(storage.TryGetFailureInfo("msg-1", out _), Is.True);
    }

    [Test]
    public void Sweep_does_not_run_before_interval()
    {
        var fakeTime = new FakeTimeProvider();
        var storage = new InMemoryFailureInfoStorage(fakeTime);

        storage.RecordFailure("msg-1", new Exception("fail"), new ContextBag());

        // Advance past TTL but not past sweep interval from last sweep
        fakeTime.Advance(TimeSpan.FromSeconds(20));

        storage.RecordFailure("msg-2", new Exception("trigger"), new ContextBag());

        // msg-1 should still be there because sweep hasn't run
        Assert.That(storage.TryGetFailureInfo("msg-1", out _), Is.True);
    }
}
