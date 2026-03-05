namespace NServiceBus.Transport.IBMMQ.PerformanceTests.Reporting;

using Metrics;

class ConsoleTableReporter : IResultReporter
{
    public void ReportResults(string scenarioName, List<PerformanceResult> results)
    {
        if (results.Count == 0)
        {
            return;
        }

        ConsoleLog.WriteLine();
        ConsoleLog.WriteLine(ConsoleLog.Bold($"=== {scenarioName} ==="));
        ConsoleLog.WriteLine();

        ConsoleLog.WriteLine(ConsoleLog.Bold(
            $"{"TxMode",-27}{"Inst",5}{"Msgs",7}{"Elapsed",10}{"Msg/sec",12}{"CPU",10}{"Handles",9}{"Alloc MB",12}{"GC 0/1/2",14}"));
        ConsoleLog.WriteLine(ConsoleLog.Dim(new string('-', 106)));

        foreach (var r in results)
        {
            ConsoleLog.WriteLine(
                $"{r.TransactionMode,-27}{r.InstanceCount,5}{r.MessageCount,7}{FormatElapsed(r.Elapsed),10}{r.MessagesPerSecond,12:F1}{FormatCpu(r.CpuTime),10}{r.Handles,9}{r.AllocatedMb,12:F1}{$"{r.GcGen0}/{r.GcGen1}/{r.GcGen2}",14}");
        }

        ConsoleLog.WriteLine();
    }

    static FormattableString FormatElapsed(TimeSpan elapsed) => $"{elapsed.TotalSeconds:F2}s";

    static FormattableString FormatCpu(TimeSpan cpu) => $"{cpu.TotalSeconds:F2}s";
}
