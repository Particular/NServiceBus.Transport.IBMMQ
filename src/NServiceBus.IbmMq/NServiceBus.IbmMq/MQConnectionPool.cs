using IBM.WMQ;
using NServiceBus.Logging;
using System.Collections.Concurrent;

namespace NServiceBus.Transport.IbmMq;

sealed class MQConnectionPool(string queueManagerName) : IDisposable
{
    static readonly ILog Log = LogManager.GetLogger<MQConnectionPool>();

    private readonly ConcurrentBag<MQQueueManager> _available = new();
    private readonly ConcurrentDictionary<MQQueueManager, bool> _all = new();
    private bool _disposed;

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
        var newConnection = new MQQueueManager(queueManagerName);
        _all.TryAdd(newConnection, true);
        return newConnection;
    }

    public void Return(MQQueueManager connection)
    {
        if (_disposed)
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

    private static void DisconnectAndDispose(MQQueueManager connection)
    {
        try
        {
            connection.Disconnect();
        }
        catch (MQException ex)
        {
            Log.Debug($"Error disconnecting connection: {ex.Message}");
        }

        try
        {
            ((IDisposable)connection).Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug($"Error disposing connection: {ex.Message}");
        }
    }
}