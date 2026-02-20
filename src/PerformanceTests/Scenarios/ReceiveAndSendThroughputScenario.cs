namespace NServiceBus.Transport.IbmMq.PerformanceTests.Scenarios;

using System.Diagnostics;
using Handlers;
using Infrastructure;
using Messages;
using Metrics;
using Reporting;

class ReceiveAndSendThroughputScenario : IPerformanceScenario
{
    public string Name => "ReceiveAndSendThroughput";

    public async Task<List<PerformanceResult>> Run(ScenarioRunSettings settings, CancellationToken cancellationToken = default)
    {
        var results = new List<PerformanceResult>();
        var endpointName = "perf.recv.send";
        var sinkEndpointName = "perf.recv.send.sink";
        var queueName = MqConnectionFactory.FormatQueueName(endpointName);
        var sinkQueueName = MqConnectionFactory.FormatQueueName(sinkEndpointName);

        foreach (var txMode in settings.TransactionModes)
        {
            foreach (var instanceCount in settings.InstanceCounts)
            {
                ConsoleLog.Write($"  {Name} [{txMode}, instances={instanceCount}]...");

                using (var adminQm = MqConnectionFactory.CreateQueueManager())
                {
                    QueueOperations.EnsureQueueExists(adminQm, queueName, settings.MessageCount + 1000);
                    QueueOperations.EnsureQueueExists(adminQm, sinkQueueName, settings.MessageCount + 1000);
                    QueueOperations.PurgeQueue(adminQm, queueName);
                    QueueOperations.PurgeQueue(adminQm, sinkQueueName);
                    QueueSeeder.Seed(adminQm, queueName, settings.MessageCount);
                    adminQm.Disconnect();
                }

                HandlerCompletion.Reset(settings.MessageCount);

                var spec = new EndpointSpec
                {
                    Name = endpointName,
                    TransactionMode = txMode,
                    HandlerTypes = [typeof(ReceiveAndSendHandler)],
                    Routing = new Dictionary<Type, string>
                    {
                        [typeof(PerfTestSinkMessage)] = sinkEndpointName
                    }
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

                    var actualReceived = HandlerCompletion.CurrentCount;
                    var msgPerSec = actualReceived / sw.Elapsed.TotalSeconds;
                    ConsoleLog.WriteLine($" {msgPerSec:F1} msg/sec ({actualReceived}/{settings.MessageCount} in {sw.Elapsed.TotalSeconds:F2}s)");

                    var afterSnapshot = ProcessMetricsCollector.TakeSnapshot();
                    var deltas = ProcessMetricsCollector.ComputeDeltas(beforeSnapshot, afterSnapshot);

                    results.Add(new PerformanceResult(
                        Name, txMode, instanceCount, actualReceived, sw.Elapsed, msgPerSec,
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
