namespace NServiceBus.Transport.IBMMQ.Tests;

using NServiceBus.Logging;
using NUnit.Framework;

[TestFixture]
class MessagePumpWorkerTests
{
    [Test]
    public async Task Faulted_pump_is_still_disposed()
    {
        var connectionAttempted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var expectedException = new InvalidOperationException("Connection failed");
        var context = new ReceiveContext(
            "queue",
            0,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.FromResult(ErrorHandleResult.Handled),
            (_, _, _) => { });
        var strategy = new NoTransactionReceiveStrategy(
            LogManager.GetLogger<ReceiveStrategy>(),
            new IBMMQMessageConverter(new MqPropertyNameEncoder(), 1208),
            context);
        using var circuitBreaker = new RepeatedFailuresOverTimeCircuitBreaker(
            "test",
            TimeSpan.FromMinutes(1),
            _ => { });
        var worker = new MessagePumpWorker(
            LogManager.GetLogger<MessagePumpWorker>(),
            strategy,
            CreateConnection,
            new MessagePumpSettings(TimeSpan.Zero),
            circuitBreaker,
            "queue",
            0);

        worker.Start();
        await connectionAttempted.Task.ConfigureAwait(false);

        var thrownException = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await worker.StopAsync().ConfigureAwait(false));
        Assert.That(thrownException, Is.SameAs(expectedException));

        Assert.DoesNotThrowAsync(
            async () => await worker.DisposeAsync().ConfigureAwait(false));
        Assert.DoesNotThrowAsync(
            async () => await worker.DisposeAsync().ConfigureAwait(false));
        return;

        MqConnection CreateConnection()
        {
            connectionAttempted.TrySetResult();
            throw expectedException;
        }
    }
}
