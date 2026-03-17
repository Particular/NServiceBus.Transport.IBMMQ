namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
using Logging;

sealed class DestinationCache<T>(ILog log, int capacity) : IDisposable where T : MQDestination
{
    readonly bool isDebugEnabled = log.IsDebugEnabled;
    readonly Lock gate = new();
    readonly Dictionary<string, LinkedListNode<(string Key, T Value)>> map = new(capacity);
    readonly LinkedList<(string Key, T Value)> list = new();
    bool disposed;

    public T GetOrAdd(string key, Func<string, T> factory)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        lock (gate)
        {
            if (map.TryGetValue(key, out var node))
            {
                list.Remove(node);
                list.AddFirst(node);
                return node.Value.Value;
            }
        }

        // Factory called outside the lock to avoid blocking all
        // concurrent dispatchers during MQ network round-trips.
        var value = factory(key);

        lock (gate)
        {
            // Another thread may have added the same key while we
            // were outside the lock, so use that entry instead.
            if (map.TryGetValue(key, out var existing))
            {
                list.Remove(existing);
                list.AddFirst(existing);
                CloseQuietly(value);
                return existing.Value.Value;
            }

            if (list.Count >= capacity)
            {
                var lru = list.Last!;
                list.RemoveLast();
                map.Remove(lru.Value.Key);
                CloseQuietly(lru.Value.Value);
            }

            var newNode = list.AddFirst((key, value));
            map[key] = newNode;
            return value;
        }
    }

    public void Evict(string key)
    {
        lock (gate)
        {
            if (map.Remove(key, out var node))
            {
                list.Remove(node);
                CloseQuietly(node.Value.Value);
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            foreach (var (_, value) in list)
            {
                CloseQuietly(value);
            }

            list.Clear();
            map.Clear();
        }
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
