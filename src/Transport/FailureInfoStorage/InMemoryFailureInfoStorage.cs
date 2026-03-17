namespace NServiceBus.Transport.IBMMQ;

using System.Collections.Concurrent;

sealed class InMemoryFailureInfoStorage : IFailureInfoStorage
{
    // Entries only need to survive for the duration of the backout/re-delivery cycle
    // within the same process. Re-delivery from SYNCPOINT backout is near-instant (the
    // message is already on the queue), so 1 minute is sufficient. A longer TTL would
    // keep potentially large ContextBag object graphs alive unnecessarily.
    static readonly long EntryTtlMs = (long)TimeSpan.FromMinutes(1).TotalMilliseconds;
    static readonly long SweepIntervalMs = (long)TimeSpan.FromSeconds(30).TotalMilliseconds;

    readonly ConcurrentDictionary<string, (FailureRecord Record, long UpdatedAt)> failures = new();
    long lastSweepAt = Environment.TickCount64;

    public void RecordFailure(string messageId, Exception exception, Extensibility.ContextBag context)
    {
        var now = Environment.TickCount64;

        failures.AddOrUpdate(
            messageId,
            _ => (new FailureRecord(1, exception, context), now),
            (_, existing) => (new FailureRecord(existing.Record.NumberOfProcessingAttempts + 1, exception, context), now));

        SweepStaleEntries();
    }

    // No TTL check needed here: if an entry exists, it is from a recent backout/re-delivery
    // cycle within this process. Re-delivery from SYNCPOINT backout is near-instant, so any
    // matching entry is still relevant. Stale entries are swept periodically via RecordFailure.
    public bool TryGetFailureInfo(string messageId, out FailureRecord? info)
    {
        if (failures.TryGetValue(messageId, out var entry))
        {
            info = entry.Record;
            return true;
        }

        info = null;
        return false;
    }

    public void ClearFailure(string messageId) =>
        failures.TryRemove(messageId, out _);

    void SweepStaleEntries()
    {
        var now = Environment.TickCount64;
        var previousSweep = Interlocked.Read(ref lastSweepAt);

        if (now - previousSweep < SweepIntervalMs)
        {
            return;
        }

        // CAS ensures only one thread sweeps at a time
        if (Interlocked.CompareExchange(ref lastSweepAt, now, previousSweep) != previousSweep)
        {
            return;
        }

        var cutoff = now - EntryTtlMs;

        foreach (var kvp in failures)
        {
            if (kvp.Value.UpdatedAt < cutoff)
            {
                failures.TryRemove(kvp.Key, out _);
            }
        }
    }
}
