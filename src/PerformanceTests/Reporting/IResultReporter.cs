namespace NServiceBus.Transport.IBMMQ.PerformanceTests.Reporting;

using Metrics;

interface IResultReporter
{
    void ReportResults(string scenarioName, List<PerformanceResult> results);
}
