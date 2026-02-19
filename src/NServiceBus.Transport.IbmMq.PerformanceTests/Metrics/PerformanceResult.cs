namespace NServiceBus.Transport.IbmMq.PerformanceTests.Metrics;

record PerformanceResult(
    string ScenarioName,
    TransportTransactionMode TransactionMode,
    int InstanceCount,
    int MessageCount,
    TimeSpan Elapsed,
    double MessagesPerSecond,
    TimeSpan CpuTime,
    int Handles,
    double AllocatedMb,
    int GcGen0,
    int GcGen1,
    int GcGen2);
