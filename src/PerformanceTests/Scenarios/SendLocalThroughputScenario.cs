namespace NServiceBus.Transport.IBMMQ.PerformanceTests.Scenarios;

using System.Diagnostics;
using Handlers;
using Infrastructure;
using Metrics;
using Reporting;

class SendLocalThroughputScenario : IPerformanceScenario
{
    public string Name => "SendLocalThroughput";

    public async Task<List<PerformanceResult>> Run(ScenarioRunSettings settings, CancellationToken cancellationToken = default)
    {
        var results = new List<PerformanceResult>();
        var endpointName = "perf.sendlocal.throughput";
        var queueName = MqConnectionFactory.FormatQueueName(endpointName);

        foreach (var txMode in settings.TransactionModes)
        {
            foreach (var instanceCount in settings.InstanceCounts)
            {
                var seedCount = instanceCount * Environment.ProcessorCount * 2 * 2;

                ConsoleLog.Write($"  {Name} [{txMode}, instances={instanceCount}, seed={seedCount}]...");

                using (var adminQm = MqConnectionFactory.CreateQueueManager())
                {
                    QueueOperations.EnsureQueueExists(adminQm, queueName, seedCount + 1000);
                    QueueOperations.PurgeQueue(adminQm, queueName);
                    QueueSeeder.Seed(adminQm, queueName, seedCount);
                    adminQm.Disconnect();
                }

                HandlerCompletion.Reset(int.MaxValue);

                var spec = new EndpointSpec
                {
                    Name = endpointName,
                    TransactionMode = txMode,
                    HandlerTypes = [typeof(SendLocalHandler)]
                };

                var endpoints = await EndpointLauncher.StartMultiple(spec, instanceCount, cancellationToken).ConfigureAwait(false);

                try
                {
                    var beforeSnapshot = ProcessMetricsCollector.TakeSnapshot();
                    var sw = Stopwatch.StartNew();
                    HandlerCompletion.OpenGate();

                    await Task.Delay(TimeSpan.FromSeconds(settings.DurationSeconds), cancellationToken).ConfigureAwait(false);

                    sw.Stop();

                    var actualProcessed = HandlerCompletion.CurrentCount;
                    var msgPerSec = actualProcessed / sw.Elapsed.TotalSeconds;
                    ConsoleLog.WriteLine($" {msgPerSec:F1} msg/sec ({actualProcessed} in {sw.Elapsed.TotalSeconds:F2}s)");

                    var afterSnapshot = ProcessMetricsCollector.TakeSnapshot();
                    var deltas = ProcessMetricsCollector.ComputeDeltas(beforeSnapshot, afterSnapshot);

                    results.Add(new PerformanceResult(
                        Name, txMode, instanceCount, actualProcessed, sw.Elapsed, msgPerSec,
                        deltas.CpuDelta, deltas.FinalHandles, deltas.AllocatedMb,
                        deltas.GcGen0Delta, deltas.GcGen1Delta, deltas.GcGen2Delta));
                }
                finally
                {
                    await EndpointLauncher.StopAll(endpoints, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return results;
    }
}
