namespace NServiceBus.Transport.IbmMq.PerformanceTests.Scenarios;

using System.Diagnostics;
using Handlers;
using Infrastructure;
using Messages;
using Metrics;
using Reporting;

class PublishThroughputScenario : IPerformanceScenario
{
    public string Name => "PublishThroughput";

    public async Task<List<PerformanceResult>> Run(ScenarioRunSettings settings, CancellationToken cancellationToken = default)
    {
        var results = new List<PerformanceResult>();
        var endpointName = "perf.publish.throughput";
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
                    adminQm.Disconnect();
                }

                HandlerCompletion.Reset(int.MaxValue);

                var spec = new EndpointSpec
                {
                    Name = endpointName,
                    TransactionMode = txMode,
                    HandlerTypes = [typeof(PublishHandler)]
                };

                // Start endpoints first so subscriptions are created before seeding
                var endpoints = await EndpointLauncher.StartMultiple(spec, instanceCount, cancellationToken).ConfigureAwait(false);

                try
                {
                    // Seed after endpoints are running so the subscription exists
                    using (var adminQm = MqConnectionFactory.CreateQueueManager())
                    {
                        QueueSeeder.Seed<PerfTestEvent>(adminQm, queueName, seedCount, "Publish");
                        adminQm.Disconnect();
                    }

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
