namespace NServiceBus.Transport.IbmMq;

using System.Collections.Concurrent;
using NServiceBus.Logging;
using IBM.WMQ;

sealed class MQConnectionPool(Func<MQQueueManager> createQueueManager) : IDisposable
{
    static readonly ILog Log = LogManager.GetLogger<MQConnectionPool>();

    readonly ConcurrentBag<MQQueueManager> _available = [];
#pragma warning disable PS0025
    readonly ConcurrentDictionary<MQQueueManager, bool> _all = new();
#pragma warning restore PS0025
    bool _disposed;

    public MQQueueManager Lease()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MQConnectionPool));
        }

        if (_available.TryTake(out var connection))
        {
            Log.Debug("Leasing existing connection from pool");
            return connection;
        }

        Log.Debug("Creating new connection for pool");
        var newConnection = createQueueManager();
        _all.TryAdd(newConnection, true);
        return newConnection;
    }

    public void Return(MQQueueManager connection, bool dirty = false)
    {
        if (_disposed || dirty)
        {
            Log.Debug("Pool disposed, disconnecting returned connection");
            DisconnectAndDispose(connection);
            return;
        }

        if (!_all.ContainsKey(connection))
        {
            Log.Warn("Attempted to return a connection not from this pool");
            return;
        }

        Log.Debug("Returning connection to pool");
        _available.Add(connection);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Log.Debug("Disposing connection pool");

        foreach (var connection in _all.Keys)
        {
            DisconnectAndDispose(connection);
        }

        _all.Clear();
    }

    static void DisconnectAndDispose(MQQueueManager connection)
    {
        try
        {
            connection.Disconnect();
        }
        catch (MQException ex)
        {
            Log.Warn("Failed to disconnect connection", ex);
        }

        try
        {
            ((IDisposable)connection).Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to dispose connection", ex);
        }
    }
}