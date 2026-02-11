namespace NServiceBus.Transport.IbmMq;

using Logging;
using Microsoft.Extensions.DependencyInjection;

sealed class IbmMqMessageReceiver(
    ILog log,
    IServiceScopeFactory scopeFactory,
    ISubscriptionManager subscriptions,
    ReceiveSettings receiveSettings
) : IMessageReceiver, IAsyncDisposable
{

    readonly List<(AsyncServiceScope Scope, MessagePumpWorker Worker)> workers = [];

    int concurrency;
    OnMessage? onMessage;
    OnError? onError;

    public ISubscriptionManager Subscriptions => subscriptions;

    public string Id => receiveSettings.Id;

    public string ReceiveAddress => receiveSettings.ReceiveAddress.BaseAddress;

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
                    var entry = CreateScopedWorker(i);
                    workers.Add(entry);
                    entry.Worker.Start();
                    log.DebugFormat("Added worker {0}", i);
                }
            }
            else if (newConcurrency < concurrency)
            {
                // Scale down - remove excess workers
                var entriesToRemove = workers.Skip(newConcurrency).ToList();
                workers.RemoveRange(newConcurrency, workers.Count - newConcurrency);

                // Stop and dispose removed workers asynchronously
                var tasks = new List<Task>(entriesToRemove.Count);
                foreach (var entry in entriesToRemove)
                {
                    tasks.Add(StopAndDisposeWorker(entry, cancellationToken));
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
                var entry = CreateScopedWorker(i);
                workers.Add(entry);

                tasks.Add(Task.Run(() => entry.Worker.Start(), cancellationToken));
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

        await receiveLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            List<(AsyncServiceScope Scope, MessagePumpWorker Worker)> entriesToStop = [.. workers];
            workers.Clear();

            var tasks = new List<Task>(entriesToStop.Count);

            // Stop all workers, passing the cancellation token so in-flight messages can be cancelled
            foreach (var entry in entriesToStop)
            {
                tasks.Add(StopAndDisposeWorker(entry, cancellationToken));
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
        foreach (var entry in workers)
        {
            await entry.Scope.DisposeAsync().ConfigureAwait(false);
        }

        receiveLock.Dispose();
    }

    (AsyncServiceScope Scope, MessagePumpWorker Worker) CreateScopedWorker(int index)
    {
        var scope = scopeFactory.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<MessagePumpWorker>();
        worker.Initialize(ReceiveAddress, onMessage!, onError!, index);
        return (scope, worker);
    }

    static async Task StopAndDisposeWorker(
        (AsyncServiceScope Scope, MessagePumpWorker Worker) entry,
        CancellationToken cancellationToken)
    {
        await entry.Worker.StopAsync(cancellationToken).ConfigureAwait(false);
        await entry.Scope.DisposeAsync().ConfigureAwait(false);
    }
}