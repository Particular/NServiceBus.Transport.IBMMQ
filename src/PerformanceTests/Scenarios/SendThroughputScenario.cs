namespace NServiceBus.Transport.IbmMq.PerformanceTests.Scenarios;

using System.Diagnostics;
using NServiceBus.Transport.IbmMq.PerformanceTests.Infrastructure;
using NServiceBus.Transport.IbmMq.PerformanceTests.Messages;
using NServiceBus.Transport.IbmMq.PerformanceTests.Metrics;

class SendThroughputScenario : IPerformanceScenario
{
    public string Name => "SendThroughput";

    public async Task<List<PerformanceResult>> Run(ScenarioRunSettings settings, CancellationToken cancellationToken = default)
    {
        var results = new List<PerformanceResult>();
        var targetQueue = "perf.send.target";
        var formattedTargetQueue = MqConnectionFactory.FormatQueueName(targetQueue);

        foreach (var txMode in settings.TransactionModes)
        {
            foreach (var instanceCount in settings.InstanceCounts)
            {
                Console.Write($"  {Name} [{txMode}, instances={instanceCount}]...");

                using (var adminQm = MqConnectionFactory.CreateQueueManager())
                {
                    QueueOperations.EnsureQueueExists(adminQm, formattedTargetQueue, settings.MessageCount + 1000);
                    QueueOperations.PurgeQueue(adminQm, formattedTargetQueue);
                    adminQm.Disconnect();
                }

                var spec = new EndpointSpec
                {
                    Name = "perf.send.throughput",
                    TransactionMode = txMode,
                    SendOnly = true
                };

                var endpoints = await EndpointLauncher.StartMultiple(spec, instanceCount, cancellationToken).ConfigureAwait(false);

                try
                {
                    var beforeSnapshot = ProcessMetricsCollector.TakeSnapshot();
                    var sw = Stopwatch.StartNew();

                    var messagesPerInstance = settings.MessageCount / instanceCount;
                    var remainder = settings.MessageCount % instanceCount;

                    var tasks = new Task[instanceCount];
                    for (int t = 0; t < instanceCount; t++)
                    {
                        var count = messagesPerInstance + (t < remainder ? 1 : 0);
                        var session = endpoints[t];
                        tasks[t] = Task.Run(async () =>
                        {
                            for (int i = 0; i < count; i++)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var sendOptions = new SendOptions();
                                sendOptions.SetDestination(targetQueue);
                                await session.Send(new PerfTestMessage { Index = i }, sendOptions, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                        }, cancellationToken);
                    }

                    await Task.WhenAll(tasks).ConfigureAwait(false);

                    sw.Stop();
                    var afterSnapshot = ProcessMetricsCollector.TakeSnapshot();
                    var deltas = ProcessMetricsCollector.ComputeDeltas(beforeSnapshot, afterSnapshot);

                    var msgPerSec = settings.MessageCount / sw.Elapsed.TotalSeconds;
                    Console.WriteLine($" {msgPerSec:F1} msg/sec ({sw.Elapsed.TotalSeconds:F2}s)");

                    results.Add(new PerformanceResult(
                        Name, txMode, instanceCount, settings.MessageCount, sw.Elapsed, msgPerSec,
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
