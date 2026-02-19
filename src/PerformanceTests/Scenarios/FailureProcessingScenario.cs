namespace NServiceBus.Transport.IbmMq.PerformanceTests.Scenarios;

using System.Diagnostics;
using NServiceBus.Logging;
using NServiceBus.Transport.IbmMq.PerformanceTests.Handlers;
using NServiceBus.Transport.IbmMq.PerformanceTests.Infrastructure;
using NServiceBus.Transport.IbmMq.PerformanceTests.Messages;
using NServiceBus.Transport.IbmMq.PerformanceTests.Metrics;

class FailureProcessingScenario : IPerformanceScenario
{
    public string Name => "FailureProcessing";

    public async Task<List<PerformanceResult>> Run(ScenarioRunSettings settings, CancellationToken cancellationToken = default)
    {
        var results = new List<PerformanceResult>();
        var endpointName = "perf.failure";
        var errorQueueName = "perf.failure.error";
        var queueName = MqConnectionFactory.FormatQueueName(endpointName);
        var formattedErrorQueue = MqConnectionFactory.FormatQueueName(errorQueueName);

        foreach (var txMode in settings.TransactionModes)
        {
            foreach (var instanceCount in settings.InstanceCounts)
            {
                Console.Write($"  {Name} [{txMode}, instances={instanceCount}]...");

                using (var adminQm = MqConnectionFactory.CreateQueueManager())
                {
                    QueueOperations.EnsureQueueExists(adminQm, queueName, settings.MessageCount + 1000);
                    QueueOperations.EnsureQueueExists(adminQm, formattedErrorQueue, settings.MessageCount + 1000);
                    QueueOperations.PurgeQueue(adminQm, queueName);
                    QueueOperations.PurgeQueue(adminQm, formattedErrorQueue);
                    QueueSeeder.Seed<PerfTestFailureMessage>(adminQm, queueName, settings.MessageCount);
                    adminQm.Disconnect();
                }

                var spec = new EndpointSpec
                {
                    Name = endpointName,
                    TransactionMode = txMode,
                    ErrorQueue = errorQueueName,
                    ImmediateRetries = 0,
                    DelayedRetries = 0,
                    HandlerTypes = [typeof(FailureHandler)],
                    LogLevel = LogLevel.Fatal
                };

                var endpoints = await EndpointLauncher.StartMultiple(spec, instanceCount, cancellationToken).ConfigureAwait(false);

                try
                {
                    var beforeSnapshot = ProcessMetricsCollector.TakeSnapshot();
                    var sw = Stopwatch.StartNew();

                    var deadline = DateTime.UtcNow.AddSeconds(settings.DurationSeconds);
                    var errorDepth = 0;

                    while (DateTime.UtcNow < deadline)
                    {
                        using var pollQm = MqConnectionFactory.CreateQueueManager();
                        errorDepth = QueueOperations.GetQueueDepth(pollQm, formattedErrorQueue);
                        pollQm.Disconnect();

                        if (errorDepth >= settings.MessageCount)
                        {
                            break;
                        }

                        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                    }

                    sw.Stop();

                    var afterSnapshot = ProcessMetricsCollector.TakeSnapshot();
                    var deltas = ProcessMetricsCollector.ComputeDeltas(beforeSnapshot, afterSnapshot);

                    var msgPerSec = errorDepth / sw.Elapsed.TotalSeconds;
                    Console.WriteLine($" {msgPerSec:F1} msg/sec ({errorDepth}/{settings.MessageCount} in {sw.Elapsed.TotalSeconds:F2}s)");

                    results.Add(new PerformanceResult(
                        Name, txMode, instanceCount, errorDepth, sw.Elapsed, msgPerSec,
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
