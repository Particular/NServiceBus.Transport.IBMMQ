namespace NServiceBus.Transport.IbmMq;

using NServiceBus.Logging;

class IbmMqMessageReceiver(
    Func<string, OnMessage, OnError, int, MessagePumpWorker> createWorker,
    ISubscriptionManager subscriptions,
    ReceiveSettings receiveSettings
) : IMessageReceiver, IAsyncDisposable
{
    readonly ILog Log = LogManager.GetLogger<IbmMqMessageReceiver>();

    readonly List<MessagePumpWorker> workers = [];

    int concurrency;
    OnMessage? onMessage;
    OnError? onError;

    public ISubscriptionManager Subscriptions => subscriptions;

    public string Id => receiveSettings.Id;

    public string ReceiveAddress => receiveSettings.ReceiveAddress.BaseAddress;

    public async Task ChangeConcurrency(PushRuntimeSettings limitations, CancellationToken cancellationToken = default)
    {
        var newConcurrency = limitations.MaxConcurrency;
        Log.DebugFormat("Changing concurrency from {0} to {1}", concurrency, newConcurrency);

        await receiveLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (newConcurrency > concurrency)
            {
                // Scale up - add more workers
                for (int i = concurrency; i < newConcurrency; i++)
                {
                    var worker = createWorker(ReceiveAddress, onMessage!, onError!, i);
                    workers.Add(worker);
                    worker.Start();
                    Log.DebugFormat("Added worker {0}", i);
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

    public Task Initialize(PushRuntimeSettings limitations, OnMessage onMessage, OnError onError, CancellationToken cancellationToken = default)
    {
        this.onMessage = onMessage;
        this.onError = onError;
        concurrency = limitations.MaxConcurrency;
        Log.DebugFormat("Initialized receiver with concurrency {0}", concurrency);
        return Task.CompletedTask;
    }

    readonly SemaphoreSlim receiveLock = new(1, 1);

    public async Task StartReceive(CancellationToken cancellationToken = default)
    {
        Log.DebugFormat("Starting to receive messages from {0} with {1} workers", ReceiveAddress, concurrency);

        await receiveLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var tasks = new List<Task>(concurrency);
            for (int i = 0; i < concurrency; i++)
            {
                var worker = createWorker(ReceiveAddress, onMessage!, onError!, i);
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
        Log.DebugFormat("Stopping {0} workers for {1}", workers.Count, ReceiveAddress);

        await receiveLock.WaitAsync(cancellationToken)
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

            Log.DebugFormat("All workers stopped for {0}", ReceiveAddress);
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

    static async Task StopAndDisposeWorker(MessagePumpWorker worker, CancellationToken cancellationToken)
    {
        await worker.StopAsync(cancellationToken).ConfigureAwait(false);
        await worker.DisposeAsync().ConfigureAwait(false);
    }
}