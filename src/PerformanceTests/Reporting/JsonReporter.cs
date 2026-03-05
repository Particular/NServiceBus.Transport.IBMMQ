namespace NServiceBus.Transport.IBMMQ.PerformanceTests.Reporting;

using System.Text.Json;
using System.Text.Json.Serialization;
using Metrics;

class JsonReporter : IResultReporter
{
    readonly string outputPath;
    readonly List<JsonResultEntry> entries = [];

    public JsonReporter(string outputPath)
    {
        this.outputPath = outputPath;
    }

    public void ReportResults(string scenarioName, List<PerformanceResult> results)
    {
        var timestamp = DateTimeOffset.UtcNow;

        foreach (var r in results)
        {
            entries.Add(new JsonResultEntry
            {
                Scenario = scenarioName,
                TransactionMode = r.TransactionMode.ToString(),
                InstanceCount = r.InstanceCount,
                MessageCount = r.MessageCount,
                ElapsedSeconds = Math.Round(r.Elapsed.TotalSeconds, 3),
                MessagesPerSecond = Math.Round(r.MessagesPerSecond, 1),
                CpuTimeSeconds = Math.Round(r.CpuTime.TotalSeconds, 3),
                Handles = r.Handles,
                AllocatedMb = Math.Round(r.AllocatedMb, 1),
                GcGen0 = r.GcGen0,
                GcGen1 = r.GcGen1,
                GcGen2 = r.GcGen2,
                Timestamp = timestamp
            });
        }
    }

    public void WriteFile()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(entries, options);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath, json);
    }

    class JsonResultEntry
    {
        public required string Scenario { get; init; }
        public required string TransactionMode { get; init; }
        public required int InstanceCount { get; init; }
        public required int MessageCount { get; init; }
        public required double ElapsedSeconds { get; init; }
        public required double MessagesPerSecond { get; init; }
        public required double CpuTimeSeconds { get; init; }
        public required int Handles { get; init; }
        public required double AllocatedMb { get; init; }
        public required int GcGen0 { get; init; }
        public required int GcGen1 { get; init; }
        public required int GcGen2 { get; init; }

        [JsonPropertyName("timestamp")]
        public required DateTimeOffset Timestamp { get; init; }
    }
}
