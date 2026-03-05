namespace NServiceBus.Transport.IBMMQ.PerformanceTests.Scenarios;

using System.Diagnostics;
using Handlers;
using Infrastructure;
using Messages;
using Metrics;
using Reporting;

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
                ConsoleLog.Write($"  {Name} [{txMode}, instances={instanceCount}]...");

                using (var adminQm = MqConnectionFactory.CreateQueueManager())
                {
                    QueueOperations.EnsureQueueExists(adminQm, queueName, settings.MessageCount + 1000);
                    QueueOperations.EnsureQueueExists(adminQm, formattedErrorQueue, settings.MessageCount + 1000);
                    QueueOperations.PurgeQueue(adminQm, queueName);
                    QueueOperations.PurgeQueue(adminQm, formattedErrorQueue);
                    QueueSeeder.Seed<PerfTestFailureMessage>(adminQm, queueName, settings.MessageCount);
                    adminQm.Disconnect();
                }

                HandlerCompletion.Reset(settings.MessageCount);

                var spec = new EndpointSpec
                {
                    Name = endpointName,
                    TransactionMode = txMode,
                    ErrorQueue = errorQueueName,
                    ImmediateRetries = 0,
                    DelayedRetries = 0,
                    NotifyOnFailure = true,
                    HandlerTypes = [typeof(FailureHandler)],
                };

                var endpoints = await EndpointLauncher.StartMultiple(spec, instanceCount, cancellationToken).ConfigureAwait(false);

                try
                {
                    var beforeSnapshot = ProcessMetricsCollector.TakeSnapshot();
                    var sw = Stopwatch.StartNew();
                    HandlerCompletion.OpenGate();

                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(settings.DurationSeconds), cancellationToken);
                    await Task.WhenAny(HandlerCompletion.Completion, timeoutTask).ConfigureAwait(false);

                    sw.Stop();

                    var actualFailed = HandlerCompletion.CurrentCount;
                    var afterSnapshot = ProcessMetricsCollector.TakeSnapshot();
                    var deltas = ProcessMetricsCollector.ComputeDeltas(beforeSnapshot, afterSnapshot);

                    var msgPerSec = actualFailed / sw.Elapsed.TotalSeconds;
                    ConsoleLog.WriteLine($" {msgPerSec:F1} msg/sec ({actualFailed}/{settings.MessageCount} in {sw.Elapsed.TotalSeconds:F2}s)");

                    results.Add(new PerformanceResult(
                        Name, txMode, instanceCount, actualFailed, sw.Elapsed, msgPerSec,
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
