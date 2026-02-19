namespace NServiceBus.Transport.IbmMq;

using System.Collections.Concurrent;

sealed class MqConnectionPool(CreateQueueManager createConnection, CreateQueueManagerFacade createFacade, int maxSize) : IAsyncDisposable
{
    readonly ConcurrentBag<MqQueueManagerFacade> pool = [];
    int currentSize;

    public MqQueueManagerFacade Rent()
    {
        if (pool.TryTake(out var facade))
        {
            return facade;
        }

        if (Interlocked.Increment(ref currentSize) <= maxSize)
        {
            return createFacade(createConnection());
        }

        Interlocked.Decrement(ref currentSize);

        // Pool exhausted — spin-wait until one is returned
        SpinWait.SpinUntil(() => pool.TryTake(out facade));

        return facade!;
    }

    public void Return(MqQueueManagerFacade facade) => pool.Add(facade);

    public ValueTask DisposeAsync()
    {
        while (pool.TryTake(out var facade))
        {
            facade.Disconnect();
        }

        return ValueTask.CompletedTask;
    }
}