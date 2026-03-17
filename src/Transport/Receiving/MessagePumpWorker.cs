namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
using Logging;
using Microsoft.Extensions.DependencyInjection;

sealed record MessagePumpSettings(TimeSpan MessageWaitInterval);

sealed class MessagePumpWorker(
    ILog log,
    IServiceScopeFactory scopeFactory,
    MessagePumpSettings settings,
    Action<string, Exception, CancellationToken> criticalError
) : IAsyncDisposable
{
    const int ReconnectBaseDelayMs = 1000;
    const int ReconnectMaxDelayMs = 60_000;

    readonly CancellationTokenSource stopCts = new();
    readonly CancellationTokenSource cancellationCts = new();
    Task? pumpTask;

    string queueName = null!;
    OnMessage onMessage = null!;
    OnError onError = null!;
    int workerIndex;

    public void Initialize(string queueName, OnMessage onMessage, OnError onError, int workerIndex)
    {
        this.queueName = queueName;
        this.onMessage = onMessage;
        this.onError = onError;
        this.workerIndex = workerIndex;
    }

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
            int reconnectAttempt = 0;

            // Outer loop: scope lifecycle (reconnection)
            while (!stopCts.IsCancellationRequested)
            {
                var scope = scopeFactory.CreateAsyncScope();
                await using var _ = scope
                    .ConfigureAwait(false);
                var strategy = scope.ServiceProvider.GetRequiredService<ReceiveStrategy>();
                strategy.Initialize(queueName, onMessage, onError, workerIndex);
                strategy.CriticalError = criticalError;

                MQQueue? queue = null;
                try
                {
                    queue = strategy.Connection.OpenInputQueue(queueName);

                    var getOptions = new MQGetMessageOptions
                    {
                        Options = strategy.GetOptionsFlags,
                        WaitInterval = (int)settings.MessageWaitInterval.TotalMilliseconds
                    };

                    reconnectAttempt = 0;

                    // Inner loop: message processing
                    while (!stopCts.IsCancellationRequested)
                    {
                        var received = await strategy.ReceiveMessage(queue, getOptions, cancellationToken)
                            .ConfigureAwait(false);

                        if (!received)
                        {
                            await Task.Yield(); // prevent tight spin on empty queue
                        }
                    }
                }
                catch (MQException ex) when (ex.ReasonCode != MQC.MQRC_NO_MSG_AVAILABLE)
                {
                    log.ErrorFormat("Worker {0} MQ error on {1} - Reason: {2}, CompCode: {3}",
                        workerIndex, queueName, ex.Reason, ex.CompCode);

                    // Scope disposal handles: MqConnection -> caches -> disconnect
                    // Fall through to outer loop which creates fresh scope

                    var maxDelay = Math.Min(ReconnectMaxDelayMs,
                        ReconnectBaseDelayMs * (1 << Math.Min(reconnectAttempt, 30)));
                    var delay = Random.Shared.Next(0, maxDelay);
                    reconnectAttempt++;

                    log.WarnFormat("Worker {0} reconnecting to {1} in {2}ms (attempt {3})",
                        workerIndex, queueName, delay, reconnectAttempt);
                    await Task.Delay(delay, cancellationToken)
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
