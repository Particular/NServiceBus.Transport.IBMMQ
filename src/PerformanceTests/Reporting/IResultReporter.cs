namespace NServiceBus.Transport.IbmMq.PerformanceTests.Reporting;

using NServiceBus.Transport.IbmMq.PerformanceTests.Metrics;

interface IResultReporter
{
    void ReportResults(string scenarioName, List<PerformanceResult> results);
}
