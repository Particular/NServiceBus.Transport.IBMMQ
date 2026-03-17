namespace NServiceBus.Transport.IBMMQ;

using System.Collections.Concurrent;

sealed class InMemoryFailureInfoStorage : IFailureInfoStorage
{
    // Re-delivery typically happens within seconds. 10 minutes is generous enough to
    // cover transient queue manager delays while still bounding memory in scale-out
    // scenarios where a different process may pick up the re-delivered message.
    static readonly long EntryTtlMs = (long)TimeSpan.FromMinutes(10).TotalMilliseconds;
    static readonly long SweepIntervalMs = (long)TimeSpan.FromMinutes(1).TotalMilliseconds;

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
