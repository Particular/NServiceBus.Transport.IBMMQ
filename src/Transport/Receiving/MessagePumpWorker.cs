namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
using Logging;
using Microsoft.Extensions.DependencyInjection;

sealed record MessagePumpSettings(TimeSpan MessageWaitInterval);

sealed class MessagePumpWorker(
    ILog log,
    IServiceScopeFactory scopeFactory,
    MessagePumpSettings settings,
    Action<string, Exception, CancellationToken> criticalError,
    RepeatedFailuresOverTimeCircuitBreaker circuitBreaker,
    string queueName,
    OnMessage onMessage,
    OnError onError,
    int workerIndex
) : IAsyncDisposable
{
    readonly CancellationTokenSource stopCts = new();
    readonly CancellationTokenSource cancellationCts = new();
    readonly ReceiveContext receiveContext = new(queueName, workerIndex, onMessage, onError, criticalError);
    Task? pumpTask;

    public void Start()
    {
        log.DebugFormat("Worker {0} starting for queue {1}", workerIndex, queueName);
        pumpTask = Task.Run(() => PumpMessages(cancellationCts.Token));
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        log.DebugFormat("Worker {0} stopping for queue {1}", workerIndex, queueName);

        CancellationTokenRegistration registration = default;
        try
        {
            if (cancellationToken.CanBeCanceled)
            {
                var cts = cancellationCts;
                registration = cancellationToken.Register(() => cts.Cancel());
            }

            await stopCts.CancelAsync()
                .ConfigureAwait(false);

            if (pumpTask != null)
            {
                await pumpTask
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            await registration.DisposeAsync()
                .ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync()
            .ConfigureAwait(false);
        cancellationCts.Dispose();
        stopCts.Dispose();
        log.DebugFormat("Worker {0} disposed", workerIndex);
    }

    async Task PumpMessages(CancellationToken cancellationToken)
    {
        var cancellationLogging = cancellationToken.Register(() =>
            log.WarnFormat("Worker {0} cancellation requested on {1}", workerIndex, queueName));

        try
        {
            // Outer loop: scope lifecycle (reconnection)
            while (!stopCts.IsCancellationRequested)
            {
                var scope = scopeFactory.CreateAsyncScope();
                await using var _ = scope
                    .ConfigureAwait(false);
                var createStrategy = scope.ServiceProvider.GetRequiredService<CreateReceiveStrategy>();
                var strategy = createStrategy(receiveContext);

                MQQueue? queue = null;
                try
                {
                    queue = strategy.Connection.OpenInputQueue(queueName);

                    var getOptions = new MQGetMessageOptions
                    {
                        Options = strategy.GetOptionsFlags,
                        WaitInterval = (int)settings.MessageWaitInterval.TotalMilliseconds
                    };

                    circuitBreaker.Success();

                    // Inner loop: message processing
                    while (!stopCts.IsCancellationRequested)
                    {
                        var received = await strategy.ReceiveMessage(queue, getOptions, cancellationToken)
                            .ConfigureAwait(false);

                        if (!received)
                        {
                            // The MQ Get() call above is a blocking wait (WaitInterval),
                            // so the thread was already yielded during the MQ wait itself.
                            // However, this entire while loop is synchronous — queue.Get()
                            // is a blocking call with no async alternative. Task.Yield()
                            // ensures this task relinquishes the thread pool thread between
                            // iterations so other queued tasks can make progress. Without it,
                            // the thread would immediately loop back into another blocking Get().
                            await Task.Yield();
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (ex is MQException mqEx)
                    {
                        log.ErrorFormat("Worker {0} MQ error on {1} - Reason: {2}, CompCode: {3}",
                            workerIndex, queueName, mqEx.Reason, mqEx.CompCode);
                    }
                    else
                    {
                        log.Error($"Worker {workerIndex} error on {queueName}", ex);
                    }

                    // Scope disposal handles: MqConnection -> caches -> disconnect
                    // Fall through to outer loop which creates fresh scope

                    await circuitBreaker.Failure(ex, cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    if (queue != null)
                    {
                        ((IDisposable)queue).Dispose();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            log.Info("Cancelled");
        }
        catch (Exception e)
        {
            log.Fatal("Message pump worker failure", e);
            throw;
        }
        finally
        {
            await cancellationLogging.DisposeAsync()
                .ConfigureAwait(false);
        }
    }
}
