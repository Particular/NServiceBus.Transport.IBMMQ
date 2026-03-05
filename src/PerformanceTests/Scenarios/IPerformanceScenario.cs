namespace NServiceBus.Transport.IBMMQ.PerformanceTests.Scenarios;

using Metrics;

record ScenarioRunSettings(
    int MessageCount,
    int DurationSeconds,
    int[] InstanceCounts,
    TransportTransactionMode[] TransactionModes);

interface IPerformanceScenario
{
    string Name { get; }

    Task<List<PerformanceResult>> Run(ScenarioRunSettings settings, CancellationToken cancellationToken = default);
}
