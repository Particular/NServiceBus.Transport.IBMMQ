namespace NServiceBus.Transport.IBMMQ;

using System.Collections.Concurrent;

sealed class MqConnectionPool : IAsyncDisposable
{
    readonly Func<MqConnection> factory;
    readonly int maxSize;
    readonly ConcurrentBag<MqConnection> pool = [];
    int currentSize;

    public MqConnectionPool(Func<MqConnection> factory, int maxSize)
    {
        this.factory = factory;
        this.maxSize = maxSize;
    }

    public MqConnection Rent()
    {
        if (pool.TryTake(out var connection))
        {
            return connection;
        }

        if (Interlocked.Increment(ref currentSize) <= maxSize)
        {
            return factory();
        }

        Interlocked.Decrement(ref currentSize);

        SpinWait.SpinUntil(() => pool.TryTake(out connection));

        return connection!;
    }

    public void Return(MqConnection connection) => pool.Add(connection);

    public void Discard(MqConnection connection)
    {
        connection.Disconnect();
        Interlocked.Decrement(ref currentSize);
    }

    public ValueTask DisposeAsync()
    {
        while (pool.TryTake(out var connection))
        {
            connection.Disconnect();
        }

        return ValueTask.CompletedTask;
    }
}
