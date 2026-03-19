namespace NServiceBus.Transport.IBMMQ.Tests;

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

[TestFixture]
public class RepeatedFailuresOverTimeCircuitBreakerTests
{
    [Test]
    public void Success_when_disarmed_does_nothing()
    {
        using var cb = new RepeatedFailuresOverTimeCircuitBreaker(
            "test", TimeSpan.FromMinutes(1), _ => Assert.Fail("Should not trigger"));

        Assert.DoesNotThrow(() => cb.Success());
    }

    [Test]
    public async Task Failure_arms_the_circuit_breaker()
    {
        var armed = false;

        using var cb = new RepeatedFailuresOverTimeCircuitBreaker(
            "test",
            TimeSpan.FromMinutes(5),
            _ => { },
            armedAction: () => armed = true,
            timeToWaitWhenArmed: TimeSpan.Zero);

        await cb.Failure(new Exception("test")).ConfigureAwait(false);

        Assert.That(armed, Is.True);
    }

    [Test]
    public async Task Success_after_failure_disarms()
    {
        var disarmed = false;

        using var cb = new RepeatedFailuresOverTimeCircuitBreaker(
            "test",
            TimeSpan.FromMinutes(5),
            _ => { },
            disarmedAction: () => disarmed = true,
            timeToWaitWhenArmed: TimeSpan.Zero);

        await cb.Failure(new Exception("test")).ConfigureAwait(false);

        cb.Success();

        Assert.That(disarmed, Is.True);
    }

    [Test]
    public async Task Triggers_after_timeout_when_failures_continue()
    {
        var triggered = new TaskCompletionSource<Exception>();

        using var cb = new RepeatedFailuresOverTimeCircuitBreaker(
            "test",
            TimeSpan.Zero, // trigger immediately
            ex => triggered.TrySetResult(ex),
            timeToWaitWhenTriggered: TimeSpan.Zero,
            timeToWaitWhenArmed: TimeSpan.FromMilliseconds(50));

        var expectedException = new InvalidOperationException("broker down");
        await cb.Failure(expectedException).ConfigureAwait(false);

        // Timer fires on a thread pool thread — give it a moment
        var result = await Task.WhenAny(triggered.Task, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);

        Assert.That(result, Is.EqualTo(triggered.Task), "Circuit breaker should have triggered");
        Assert.That(triggered.Task.Result, Is.SameAs(expectedException));
    }

    [Test]
    public async Task Does_not_trigger_if_success_before_timeout()
    {
        var triggered = false;

        using var cb = new RepeatedFailuresOverTimeCircuitBreaker(
            "test",
            TimeSpan.FromMilliseconds(200),
            _ => triggered = true,
            timeToWaitWhenArmed: TimeSpan.Zero);

        await cb.Failure(new Exception("test")).ConfigureAwait(false);

        cb.Success();

        await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);

        Assert.That(triggered, Is.False);
    }

    [Test]
    public async Task Subsequent_failures_when_triggered_use_longer_delay()
    {
        using var cb = new RepeatedFailuresOverTimeCircuitBreaker(
            "test",
            TimeSpan.Zero,
            _ => { },
            timeToWaitWhenTriggered: TimeSpan.FromMilliseconds(100),
            timeToWaitWhenArmed: TimeSpan.Zero);

        // First failure arms
        await cb.Failure(new Exception("test")).ConfigureAwait(false);

        // Wait for trigger
        await Task.Delay(50).ConfigureAwait(false);

        // This failure should use the triggered delay (100ms)
        var sw = Stopwatch.StartNew();
        await cb.Failure(new Exception("test")).ConfigureAwait(false);
        sw.Stop();

        Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(80),
            "Delay after trigger should use the longer timeToWaitWhenTriggered");
    }

    [Test]
    public async Task Can_rearm_after_disarm()
    {
        var armCount = 0;

        using var cb = new RepeatedFailuresOverTimeCircuitBreaker(
            "test",
            TimeSpan.FromMinutes(5),
            _ => { },
            armedAction: () => Interlocked.Increment(ref armCount),
            timeToWaitWhenArmed: TimeSpan.Zero);

        await cb.Failure(new Exception("first")).ConfigureAwait(false);
        Assert.That(armCount, Is.EqualTo(1));

        cb.Success();

        await cb.Failure(new Exception("second")).ConfigureAwait(false);
        Assert.That(armCount, Is.EqualTo(2));
    }
}
