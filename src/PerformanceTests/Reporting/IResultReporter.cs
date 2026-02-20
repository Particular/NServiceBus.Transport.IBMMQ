namespace NServiceBus.Transport.IbmMq.PerformanceTests.Reporting;

using Metrics;

interface IResultReporter
{
    void ReportResults(string scenarioName, List<PerformanceResult> results);
}
