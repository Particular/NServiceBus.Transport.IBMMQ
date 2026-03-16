namespace NServiceBus.Transport.IBMMQ;

using System.Collections.Concurrent;
using Logging;
using Microsoft.Extensions.DependencyInjection;

sealed class IBMMQMessageReceiver(
    ILog log,
    IServiceScopeFactory scopeFactory,
    ISubscriptionManager subscriptions,
    ReceiveSettings receiveSettings,
    MessagePumpSettings pumpSettings,
    SanitizeResourceName resourceNameFormatter,
    Action<string, Exception, CancellationToken> criticalError
) : IMessageReceiver, IAsyncDisposable
{

    readonly List<MessagePumpWorker> workers = [];
    readonly string formattedReceiveAddress = resourceNameFormatter(ToTransportAddress(receiveSettings.ReceiveAddress));

    // Shared across all workers to coordinate SendsAtomicWithReceive error handling
    readonly ConcurrentDictionary<string, (Exception Exception, Extensibility.ContextBag ContextBag)>? failedMessages =
        pumpSettings.TransactionMode == TransportTransactionMode.SendsAtomicWithReceive
            ? new() : null;

    // Shared across all workers to track per-message failure counts for ReceiveOnly/None modes
    readonly ConcurrentDictionary<string, int>? failureCounts =
        pumpSettings.TransactionMode != TransportTransactionMode.SendsAtomicWithReceive
            ? new() : null;

    int concurrency;
    OnMessage? onMessage;
    OnError? onError;

    public ISubscriptionManager Subscriptions => subscriptions;

    public string Id => receiveSettings.Id;

    public string ReceiveAddress => ToTransportAddress(receiveSettings.ReceiveAddress);

    public async Task ChangeConcurrency(PushRuntimeSettings limitations, CancellationToken cancellationToken = default)
    {
        var newConcurrency = limitations.MaxConcurrency;
        log.DebugFormat("Changing concurrency from {0} to {1}", concurrency, newConcurrency);

        await receiveLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (newConcurrency > concurrency)
            {
                // Scale up - add more workers
                for (int i = concurrency; i < newConcurrency; i++)
                {
                    var worker = CreateWorker(i);
                    workers.Add(worker);
                    worker.Start();
                    log.DebugFormat("Added worker {0}", i);
                }
            }
            else if (newConcurrency < concurrency)
            {
                // Scale down - remove excess workers
                var workersToRemove = workers.Skip(newConcurrency).ToList();
                workers.RemoveRange(newConcurrency, workers.Count - newConcurrency);

                // Stop and dispose removed workers asynchronously
                var tasks = new List<Task>(workersToRemove.Count);
                foreach (var worker in workersToRemove)
                {
                    tasks.Add(StopAndDisposeWorker(worker, cancellationToken));
                }

                await Task.WhenAll(tasks)
                    .ConfigureAwait(false);
            }

            concurrency = newConcurrency;
        }
        finally
        {
            receiveLock.Release();
        }
    }

    public Task Initialize(
        PushRuntimeSettings limitations,
        OnMessage onMessage,
        OnError onError,
        CancellationToken cancellationToken = default
    )
    {
        this.onMessage = onMessage;
        this.onError = onError;
        concurrency = limitations.MaxConcurrency;
        log.DebugFormat("Initialized receiver with concurrency {0}", concurrency);
        return Task.CompletedTask;
    }

    readonly SemaphoreSlim receiveLock = new(1, 1);

    public async Task StartReceive(CancellationToken cancellationToken = default)
    {
        log.DebugFormat("Starting to receive messages from {0} with {1} workers", ReceiveAddress, concurrency);

        await receiveLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var tasks = new List<Task>(concurrency);
            for (int i = 0; i < concurrency; i++)
            {
                var worker = CreateWorker(i);
                workers.Add(worker);

                tasks.Add(Task.Run(() => worker.Start(), cancellationToken));
            }

            await Task.WhenAll(tasks)
                .ConfigureAwait(false);
        }
        finally
        {
            receiveLock.Release();
        }
    }

    public async Task StopReceive(CancellationToken cancellationToken = default)
    {
        log.DebugFormat("Stopping {0} workers for {1}", workers.Count, ReceiveAddress);

        await receiveLock.WaitAsync(CancellationToken.None)
            .ConfigureAwait(false);

        try
        {
            List<MessagePumpWorker> workersToStop = [.. workers];
            workers.Clear();

            var tasks = new List<Task>(workersToStop.Count);

            // Stop all workers, passing the cancellation token so in-flight messages can be cancelled
            foreach (var worker in workersToStop)
            {
                tasks.Add(StopAndDisposeWorker(worker, cancellationToken));
            }

            await Task.WhenAll(tasks)
                .ConfigureAwait(false);

            log.DebugFormat("All workers stopped for {0}", ReceiveAddress);
        }
        finally
        {
            receiveLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Should already be done, so dispose is quick, no need for concurrent disposal
        foreach (var worker in workers)
        {
            await worker.DisposeAsync().ConfigureAwait(false);
        }

        receiveLock.Dispose();
    }

    MessagePumpWorker CreateWorker(int index)
    {
        var worker = new MessagePumpWorker(
            LogManager.GetLogger<MessagePumpWorker>(),
            scopeFactory, pumpSettings, criticalError);
        worker.Initialize(formattedReceiveAddress, onMessage!, onError!, index, failedMessages, failureCounts);
        return worker;
    }

    static async Task StopAndDisposeWorker(
        MessagePumpWorker worker,
        CancellationToken cancellationToken)
    {
        await worker.StopAsync(cancellationToken).ConfigureAwait(false);
        await worker.DisposeAsync().ConfigureAwait(false);
    }

    internal static string ToTransportAddress(QueueAddress address)
    {
        var queue = address.BaseAddress;
        if (!string.IsNullOrEmpty(address.Discriminator))
        {
            queue += "." + address.Discriminator;
        }

        if (!string.IsNullOrEmpty(address.Qualifier))
        {
            queue += "." + address.Qualifier;
        }

        return queue;
    }
}
