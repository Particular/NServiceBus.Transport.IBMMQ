namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;

sealed class DestinationCache<T> : IDisposable where T : MQDestination
{
    readonly Lock _lock = new();
    readonly Dictionary<string, LinkedListNode<(string Key, T Value)>> _map;
    readonly LinkedList<(string Key, T Value)> _list = new();
    readonly int _capacity;

    public DestinationCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _map = new(capacity);
    }

    public T GetOrAdd(string key, Func<string, T> factory)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _list.AddFirst(node);
                return node.Value.Value;
            }
        }

        // Factory called outside the lock to avoid blocking all
        // concurrent dispatchers during MQ network round-trips.
        var value = factory(key);

        lock (_lock)
        {
            // Another thread may have added the same key while we
            // were outside the lock — use that entry instead.
            if (_map.TryGetValue(key, out var existing))
            {
                _list.Remove(existing);
                _list.AddFirst(existing);
                CloseQuietly(value);
                return existing.Value.Value;
            }

            if (_list.Count >= _capacity)
            {
                var lru = _list.Last!;
                _list.RemoveLast();
                _map.Remove(lru.Value.Key);
                CloseQuietly(lru.Value.Value);
            }

            var newNode = _list.AddFirst((key, value));
            _map[key] = newNode;
            return value;
        }
    }

    public void Evict(string key)
    {
        lock (_lock)
        {
            if (_map.Remove(key, out var node))
            {
                _list.Remove(node);
                CloseQuietly(node.Value.Value);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var (_, value) in _list)
            {
                CloseQuietly(value);
            }

            _list.Clear();
            _map.Clear();
        }
    }

    static void CloseQuietly(T destination)
    {
        try
        {
            destination.Close();
        }
        catch (MQException)
        {
            // Connection may already be closed during shutdown
        }
    }
}
