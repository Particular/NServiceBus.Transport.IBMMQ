namespace NServiceBus.Transport.IbmMq.PerformanceTests.Metrics;

using System.Diagnostics;

static class ProcessMetricsCollector
{
    public static MetricsSnapshot TakeSnapshot()
    {
        var process = Process.GetCurrentProcess();

        return new MetricsSnapshot(
            TotalProcessorTime: process.TotalProcessorTime,
            HandleCount: process.HandleCount,
            TotalAllocatedBytes: GC.GetTotalAllocatedBytes(precise: false),
            GcGen0: GC.CollectionCount(0),
            GcGen1: GC.CollectionCount(1),
            GcGen2: GC.CollectionCount(2),
            ThreadCount: process.Threads.Count);
    }

    public static (TimeSpan CpuDelta, int HandleDelta, int FinalHandles, double AllocatedMb, int GcGen0Delta, int GcGen1Delta, int GcGen2Delta) ComputeDeltas(MetricsSnapshot before, MetricsSnapshot after)
    {
        return (
            CpuDelta: after.TotalProcessorTime - before.TotalProcessorTime,
            HandleDelta: after.HandleCount - before.HandleCount,
            FinalHandles: after.HandleCount,
            AllocatedMb: (after.TotalAllocatedBytes - before.TotalAllocatedBytes) / (1024.0 * 1024.0),
            GcGen0Delta: after.GcGen0 - before.GcGen0,
            GcGen1Delta: after.GcGen1 - before.GcGen1,
            GcGen2Delta: after.GcGen2 - before.GcGen2);
    }
}
