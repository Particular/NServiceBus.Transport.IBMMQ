using NServiceBus.Logging;

namespace NServiceBus.Transport.IbmMq;

internal class IbmMqMessageReceiver(
    MQConnectionPool connectionPool,
    ReceiveSettings receiveSettings
) : IMessageReceiver
{
    readonly ILog Log = LogManager.GetLogger<IbmMqMessageReceiver>();

    readonly List<MessagePumpWorker> workers = new();
    readonly object _workersLock = new();

    int concurrency = 1;
    OnMessage? onMessage;
    OnError? onError;

    public ISubscriptionManager Subscriptions => new IbmMqSubscriptionManager(connectionPool, ReceiveAddress);

    public string Id => receiveSettings.Id;

    public string ReceiveAddress => receiveSettings.ReceiveAddress.BaseAddress;

    public Task ChangeConcurrency(PushRuntimeSettings limitations, CancellationToken cancellationToken = default)
    {
        var newConcurrency = limitations.MaxConcurrency;
        Log.DebugFormat("Changing concurrency from {0} to {1}", concurrency, newConcurrency);

        lock (_workersLock)
        {
            if (newConcurrency > concurrency)
            {
                // Scale up - add more workers
                for (int i = concurrency; i < newConcurrency; i++)
                {
                    var worker = new MessagePumpWorker(connectionPool, ReceiveAddress, onMessage!, onError!, i);
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
                Task.Run(async () =>
                {
                    foreach (var worker in workersToRemove)
                    {
                        await worker.DisposeAsync().ConfigureAwait(false);
                    }
                });
            }

            concurrency = newConcurrency;
        }

        return Task.CompletedTask;
    }

    public Task Initialize(PushRuntimeSettings limitations, OnMessage onMessage, OnError onError, CancellationToken cancellationToken = default)
    {
        this.onMessage = onMessage;
        this.onError = onError;
        concurrency = limitations.MaxConcurrency;
        Log.DebugFormat("Initialized receiver with concurrency {0}", concurrency);
        return Task.CompletedTask;
    }

    public Task StartReceive(CancellationToken cancellationToken = default)
    {
        Log.DebugFormat("Starting to receive messages from {0} with {1} workers", ReceiveAddress, concurrency);

        lock (_workersLock)
        {
            for (int i = 0; i < concurrency; i++)
            {
                var worker = new MessagePumpWorker(connectionPool, ReceiveAddress, onMessage!, onError!, i);
                workers.Add(worker);
                worker.Start();
            }
        }

        return Task.CompletedTask;
    }

    public async Task StopReceive(CancellationToken cancellationToken = default)
    {
        Log.DebugFormat("Stopping {0} workers for {1}", workers.Count, ReceiveAddress);

        List<MessagePumpWorker> workersToStop;
        lock (_workersLock)
        {
            workersToStop = workers.ToList();
            workers.Clear();
        }

        // Stop all workers, passing the cancellation token so in-flight messages can be cancelled
        foreach (var worker in workersToStop)
        {
            await worker.StopAsync(cancellationToken).ConfigureAwait(false);
            await worker.DisposeAsync().ConfigureAwait(false);
        }

        Log.DebugFormat("All workers stopped for {0}", ReceiveAddress);
    }
}