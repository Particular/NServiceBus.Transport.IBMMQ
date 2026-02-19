namespace NServiceBus.Transport.IbmMq.PerformanceTests.Metrics;

record MetricsSnapshot(
    TimeSpan TotalProcessorTime,
    int HandleCount,
    long TotalAllocatedBytes,
    int GcGen0,
    int GcGen1,
    int GcGen2,
    int ThreadCount);
