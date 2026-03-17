namespace NServiceBus.Transport.IBMMQ;

using System.Collections.Concurrent;
using Logging;

/// <summary>
/// Pools MQ connections to mitigate network latency when connecting to a remote MQ broker.
/// If this pool becomes a bottleneck under high concurrency, hosting a local MQ instance
/// will reduce connection overhead more effectively than increasing the pool size.
/// </summary>
sealed class MqConnectionPool : IAsyncDisposable
{
    static readonly TimeSpan RentTimeout = TimeSpan.FromSeconds(30);

    readonly ILog log;
    readonly Func<MqConnection> factory;
    readonly int maxSize;
    readonly ConcurrentBag<MqConnection> pool = [];
    int currentSize;

    public MqConnectionPool(ILog log, Func<MqConnection> factory, int maxSize)
    {
        this.log = log;
        this.factory = factory;
        this.maxSize = maxSize;
    }

    public MqConnection Rent(CancellationToken cancellationToken = default)
    {
        if (pool.TryTake(out var connection))
        {
            return connection;
        }

        if (Interlocked.Increment(ref currentSize) <= maxSize)
        {
            try
            {
                return factory();
            }
            catch
            {
                Interlocked.Decrement(ref currentSize);
                throw;
            }
        }

        Interlocked.Decrement(ref currentSize);

        log.WarnFormat("Send connection pool limit ({0}) reached; waiting for a connection to be returned. " +
            "Consider hosting a local MQ instance to reduce connection overhead.", maxSize);

        // SpinWait is intentional here: Rent is called on synchronous send paths where an
        // async wait is not possible. SpinUntil yields the thread after initial spins, so
        // CPU cost is acceptable for the short durations connections are held during Put.
        MqConnection? waiting = null;
        if (!SpinWait.SpinUntil(() => cancellationToken.IsCancellationRequested || pool.TryTake(out waiting), RentTimeout))
        {
            log.Error($"Timed out waiting for a pooled connection after {RentTimeout.TotalSeconds}s — possible connection leak.");
            throw new TimeoutException($"Timed out waiting for a pooled connection after {RentTimeout.TotalSeconds}s — possible connection leak.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        return waiting ?? throw new InvalidOperationException("SpinUntil completed without acquiring a connection.");
    }

    public void Return(MqConnection connection) => pool.Add(connection);

    public void Discard(MqConnection connection)
    {
        try
        {
            connection.Disconnect();
        }
        finally
        {
            Interlocked.Decrement(ref currentSize);
        }
    }

    /// <remarks>
    /// Callers must return or discard all rented connections before calling DisposeAsync.
    /// Only connections sitting in the pool are disconnected. Any connections still rented
    /// at this point are owned by their callers — the dispatcher's try/finally guarantees
    /// Return or Discard, so under normal shutdown (where all dispatches complete before
    /// infrastructure disposal) no connections leak. If a caller violates this contract,
    /// the rented connection's finalizer will eventually clean up the underlying MQQueueManager,
    /// but the pool has no mechanism to wait for outstanding rentals.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        while (pool.TryTake(out var connection))
        {
            try
            {
                connection.Disconnect();
            }
            catch (Exception ex)
            {
                log.Warn("Failed to disconnect pooled connection during disposal.", ex);
            }
        }

        return ValueTask.CompletedTask;
    }
}
