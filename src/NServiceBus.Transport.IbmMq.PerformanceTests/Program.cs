using System.CommandLine;
using NServiceBus;
using NServiceBus.Transport.IbmMq.PerformanceTests.Reporting;
using NServiceBus.Transport.IbmMq.PerformanceTests.Scenarios;

var scenariosOption = new Option<string[]>("--scenarios")
{
    Description = "Scenarios to run: send, receive, receiveandsend, failure, all",
    DefaultValueFactory = _ => ["all"],
    AllowMultipleArgumentsPerToken = true,
    Arity = ArgumentArity.ZeroOrMore
};

var transactionModesOption = new Option<string[]>("--transaction-modes")
{
    Description = "Transaction modes: None, ReceiveOnly, SendsAtomicWithReceive, all",
    DefaultValueFactory = _ => ["all"],
    AllowMultipleArgumentsPerToken = true,
    Arity = ArgumentArity.ZeroOrMore
};

var messageCountOption = new Option<int>("--message-count")
{
    Description = "Number of messages for throughput tests",
    DefaultValueFactory = _ => 2500
};

var durationOption = new Option<int>("--duration-seconds")
{
    Description = "Timeout for time-based tests",
    DefaultValueFactory = _ => 30
};

var instanceCountsOption = new Option<int[]>("--instance-counts")
{
    Description = "Number of endpoint instances to run per scenario",
    DefaultValueFactory = _ => [1, 2, 4, 8],
    AllowMultipleArgumentsPerToken = true,
    Arity = ArgumentArity.ZeroOrMore
};

var rootCommand = new RootCommand("NServiceBus IBM MQ Transport Performance Tests")
{
    scenariosOption,
    transactionModesOption,
    messageCountOption,
    durationOption,
    instanceCountsOption
};

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var scenarios = parseResult.GetValue(scenariosOption)!;
    var txModeStrings = parseResult.GetValue(transactionModesOption)!;
    var messageCount = parseResult.GetValue(messageCountOption);
    var durationSeconds = parseResult.GetValue(durationOption);
    var instanceCounts = parseResult.GetValue(instanceCountsOption)!;

    var transactionModes = ParseTransactionModes(txModeStrings);
    var scenarioInstances = ResolveScenarios(scenarios);

    Console.WriteLine("IBM MQ Transport Performance Tests");
    Console.WriteLine($"  Messages: {messageCount}");
    Console.WriteLine($"  Timeout: {durationSeconds}s");
    Console.WriteLine($"  Transaction modes: {string.Join(", ", transactionModes)}");
    Console.WriteLine($"  Instance counts: {string.Join(", ", instanceCounts)}");
    Console.WriteLine($"  Scenarios: {string.Join(", ", scenarioInstances.Select(s => s.Name))}");
    Console.WriteLine();

    var reporter = new ConsoleTableReporter();

    // Warm-up: one discarded run per scenario
    var warmUpCount = Math.Max(50, messageCount / 10);
    var warmUpDuration = Math.Min(durationSeconds, 5);
    Console.WriteLine($"Warm-up ({warmUpCount} messages, {warmUpDuration}s, first txMode, first instance count)...");

    foreach (var scenario in scenarioInstances)
    {
        try
        {
            var warmUpSettings = new ScenarioRunSettings(
                warmUpCount,
                warmUpDuration,
                [instanceCounts[0]],
                [transactionModes[0]]);

            await scenario.Run(warmUpSettings, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Warm-up WARNING for {scenario.Name}: {ex.Message}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("Measured runs...");
    Console.WriteLine();

    // Measured runs
    var runSettings = new ScenarioRunSettings(messageCount, durationSeconds, instanceCounts, transactionModes);

    foreach (var scenario in scenarioInstances)
    {
        try
        {
            var results = await scenario.Run(runSettings, cancellationToken).ConfigureAwait(false);
            reporter.ReportResults(scenario.Name, results);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ERROR in {scenario.Name}: {ex.Message}");
            Console.WriteLine();
        }
    }

    Console.WriteLine("Done.");
});

var config = new CommandLineConfiguration(rootCommand);
return await config.InvokeAsync(args).ConfigureAwait(false);

static TransportTransactionMode[] ParseTransactionModes(string[] modeStrings)
{
    TransportTransactionMode[] allModes =
    [
        TransportTransactionMode.None,
        TransportTransactionMode.ReceiveOnly,
        TransportTransactionMode.SendsAtomicWithReceive
    ];

    if (modeStrings.Any(m => m.Equals("all", StringComparison.OrdinalIgnoreCase)))
    {
        return allModes;
    }

    var result = new List<TransportTransactionMode>();
    foreach (var s in modeStrings)
    {
        if (Enum.TryParse<TransportTransactionMode>(s, ignoreCase: true, out var mode))
        {
            result.Add(mode);
        }
        else
        {
            Console.WriteLine($"Warning: Unknown transaction mode '{s}', skipping.");
        }
    }

    return result.Count > 0 ? [.. result] : allModes;
}

static List<IPerformanceScenario> ResolveScenarios(string[] scenarioNames)
{
    var all = new Dictionary<string, IPerformanceScenario>(StringComparer.OrdinalIgnoreCase)
    {
        ["send"] = new SendThroughputScenario(),
        ["receive"] = new ReceiveThroughputScenario(),
        ["receiveandsend"] = new ReceiveAndSendThroughputScenario(),
        ["failure"] = new FailureProcessingScenario()
    };

    if (scenarioNames.Any(s => s.Equals("all", StringComparison.OrdinalIgnoreCase)))
    {
        return [.. all.Values];
    }

    var result = new List<IPerformanceScenario>();
    foreach (var name in scenarioNames)
    {
        if (all.TryGetValue(name, out var scenario))
        {
            result.Add(scenario);
        }
        else
        {
            Console.WriteLine($"Warning: Unknown scenario '{name}', skipping.");
        }
    }

    return result.Count > 0 ? result : [.. all.Values];
}
