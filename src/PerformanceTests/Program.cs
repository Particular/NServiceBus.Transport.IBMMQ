using System.CommandLine;
using NServiceBus.Logging;
using NServiceBus.Transport.IBMMQ.PerformanceTests.Reporting;
using NServiceBus.Transport.IBMMQ.PerformanceTests.Scenarios;

var defaultFactory = LogManager.Use<DefaultFactory>();
defaultFactory.Level(LogLevel.Fatal);

var scenariosOption = new Option<string[]>("--scenarios")
{
    Description = "Scenarios to run: send, receive, receiveandsend, failure, sendlocal, publish, all",
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

var outputOption = new Option<string?>("--output")
{
    Description = "Path for JSON results file (JSON only written when specified)"
};

var rootCommand = new RootCommand("NServiceBus IBM MQ Transport Performance Tests")
{
    scenariosOption,
    transactionModesOption,
    messageCountOption,
    durationOption,
    instanceCountsOption,
    outputOption
};

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var scenarios = parseResult.GetValue(scenariosOption)!;
    var txModeStrings = parseResult.GetValue(transactionModesOption)!;
    var messageCount = parseResult.GetValue(messageCountOption);
    var durationSeconds = parseResult.GetValue(durationOption);
    var instanceCounts = parseResult.GetValue(instanceCountsOption)!;

    var outputPath = parseResult.GetValue(outputOption);

    var transactionModes = ParseTransactionModes(txModeStrings);
    var scenarioInstances = ResolveScenarios(scenarios);

    ConsoleLog.WriteLine(ConsoleLog.Bold("IBM MQ Transport Performance Tests"));
    ConsoleLog.WriteLine($"  Messages: {messageCount}");
    ConsoleLog.WriteLine($"  Timeout: {durationSeconds}s");
    ConsoleLog.WriteLine($"  Transaction modes: {string.Join(", ", transactionModes)}");
    ConsoleLog.WriteLine($"  Instance counts: {string.Join(", ", instanceCounts)}");
    ConsoleLog.WriteLine($"  Scenarios: {string.Join(", ", scenarioInstances.Select(s => s.Name))}");
    ConsoleLog.WriteLine();

    var consoleReporter = new ConsoleTableReporter();
    JsonReporter? jsonReporter = !string.IsNullOrEmpty(outputPath) ? new JsonReporter(outputPath) : null;

    // Warm-up: one discarded run per scenario
    var warmUpCount = Math.Max(50, messageCount / 10);
    var warmUpDuration = Math.Min(durationSeconds, 5);
    ConsoleLog.WriteLine($"Warm-up ({warmUpCount} messages, {warmUpDuration}s, first txMode, first instance count)...");

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
            ConsoleLog.WriteLine(ConsoleLog.Yellow($"  Warm-up WARNING for {scenario.Name}: {ex.Message}"));
        }
    }

    ConsoleLog.WriteLine();
    ConsoleLog.WriteLine("Measured runs...");
    ConsoleLog.WriteLine();

    // Measured runs
    var runSettings = new ScenarioRunSettings(messageCount, durationSeconds, instanceCounts, transactionModes);

    foreach (var scenario in scenarioInstances)
    {
        try
        {
            var results = await scenario.Run(runSettings, cancellationToken).ConfigureAwait(false);
            consoleReporter.ReportResults(scenario.Name, results);
            jsonReporter?.ReportResults(scenario.Name, results);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ConsoleLog.WriteLine(ConsoleLog.Red($"  ERROR in {scenario.Name}: {ex.Message}"));
            ConsoleLog.WriteLine();
        }
    }

    if (jsonReporter != null)
    {
        jsonReporter.WriteFile();
        ConsoleLog.WriteLine($"Results written to {outputPath}");
    }

    ConsoleLog.WriteLine("Done.");
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
            ConsoleLog.WriteLine(ConsoleLog.Yellow($"Warning: Unknown transaction mode '{s}', skipping."));
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
        ["failure"] = new FailureProcessingScenario(),
        ["sendlocal"] = new SendLocalThroughputScenario(),
        ["publish"] = new PublishThroughputScenario()
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
            ConsoleLog.WriteLine(ConsoleLog.Yellow($"Warning: Unknown scenario '{name}', skipping."));
        }
    }

    return result.Count > 0 ? result : [.. all.Values];
}
