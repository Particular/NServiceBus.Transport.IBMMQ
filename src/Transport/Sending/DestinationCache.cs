namespace NServiceBus.Transport.IBMMQ;

using BitFaster.Caching.Lru;
using IBM.WMQ;
using Logging;

sealed class DestinationCache<T> : IDisposable where T : MQDestination
{
    readonly ILog log;
    readonly bool isDebugEnabled;
    readonly ConcurrentLru<string, T> cache;
    bool disposed;

    public DestinationCache(ILog log, int capacity)
    {
        this.log = log;
        isDebugEnabled = log.IsDebugEnabled;
        cache = new ConcurrentLru<string, T>(capacity);
        cache.Events.Value!.ItemRemoved += (_, e) => CloseQuietly(e.Value!);
    }

    public T GetOrAdd(string key, Func<string, T> factory)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return cache.GetOrAdd(key, factory);
    }

    public void Evict(string key) => cache.TryRemove(key, out _);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        cache.Clear();
    }

    void CloseQuietly(T destination)
    {
        try
        {
            destination.Close();
        }
        catch (MQException ex)
        {
            if (isDebugEnabled)
            {
                // Handle may be stale if the underlying connection was closed
                log.DebugFormat("Failed to close {0} handle: reason code {1} {2}", typeof(T).Name, ex.ReasonCode, ex.Reason);
            }
        }
    }
}
