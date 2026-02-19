namespace NServiceBus.Transport.IbmMq.PerformanceTests.Reporting;

using NServiceBus.Transport.IbmMq.PerformanceTests.Metrics;

class ConsoleTableReporter : IResultReporter
{
    public void ReportResults(string scenarioName, List<PerformanceResult> results)
    {
        if (results.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"=== {scenarioName} ===");
        Console.WriteLine();

        Console.WriteLine(
            $"{"TxMode",-27}{"Inst",5}{"Msgs",7}{"Elapsed",10}{"Msg/sec",12}{"CPU",10}{"Handles",9}{"Alloc MB",12}{"GC 0/1/2",14}");
        Console.WriteLine(new string('-', 106));

        foreach (var r in results)
        {
            Console.WriteLine(
                $"{r.TransactionMode,-27}{r.InstanceCount,5}{r.MessageCount,7}{FormatElapsed(r.Elapsed),10}{r.MessagesPerSecond,12:F1}{FormatCpu(r.CpuTime),10}{r.Handles,9}{r.AllocatedMb,12:F1}{r.GcGen0 + "/" + r.GcGen1 + "/" + r.GcGen2,14}");
        }

        Console.WriteLine();
    }

    static string FormatElapsed(TimeSpan elapsed)
    {
        return $"{elapsed.TotalSeconds:F2}s";
    }

    static string FormatCpu(TimeSpan cpu)
    {
        return $"{cpu.TotalSeconds:F2}s";
    }
}
