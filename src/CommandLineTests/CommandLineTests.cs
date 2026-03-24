namespace NServiceBus.Transport.IBMMQ.CommandLine.Tests;

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

[TestFixture]
public class CommandLineTests
{
    [Test]
    public async Task Returns_help_when_run_without_arguments()
    {
        var (output, error, exitCode) = await Execute("").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(0));
            Assert.That(error, Is.EqualTo(string.Empty));
            Assert.That(output, Does.Contain("endpoint"));
            Assert.That(output, Does.Contain("queue"));
        });
    }

    [Test]
    public async Task Endpoint_returns_help_when_run_without_subcommand()
    {
        var (output, error, exitCode) = await Execute("endpoint").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(0));
            Assert.That(error, Is.EqualTo(string.Empty));
            Assert.That(output, Does.Contain("create"));
            Assert.That(output, Does.Contain("subscribe"));
            Assert.That(output, Does.Contain("unsubscribe"));
        });
    }

    [Test]
    public async Task Queue_returns_help_when_run_without_subcommand()
    {
        var (output, error, exitCode) = await Execute("queue").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(0));
            Assert.That(error, Is.EqualTo(string.Empty));
            Assert.That(output, Does.Contain("create"));
            Assert.That(output, Does.Contain("delete"));
        });
    }

    [Test]
    public async Task Endpoint_create_requires_name_argument()
    {
        var (_, error, exitCode) = await Execute("endpoint create").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Not.EqualTo(0));
            Assert.That(error, Does.Contain("name"));
        });
    }

    [Test]
    public async Task Endpoint_subscribe_requires_name_and_event_type()
    {
        var (_, _, exitCode) = await Execute("endpoint subscribe").ConfigureAwait(false);

        Assert.That(exitCode, Is.Not.EqualTo(0));
    }

    [Test]
    public async Task Endpoint_subscribe_requires_event_type()
    {
        var (_, error, exitCode) = await Execute("endpoint subscribe MyEndpoint").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Not.EqualTo(0));
            Assert.That(error, Does.Contain("event-type"));
        });
    }

    [Test]
    public async Task Queue_create_requires_name_argument()
    {
        var (_, error, exitCode) = await Execute("queue create").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Not.EqualTo(0));
            Assert.That(error, Does.Contain("name"));
        });
    }

    [Test]
    public async Task Queue_delete_requires_name_argument()
    {
        var (_, error, exitCode) = await Execute("queue delete").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Not.EqualTo(0));
            Assert.That(error, Does.Contain("name"));
        });
    }

    [Test]
    public async Task Help_flag_shows_connection_options()
    {
        var (output, _, _) = await Execute("--help").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(output, Does.Contain("--host"));
            Assert.That(output, Does.Contain("--port"));
            Assert.That(output, Does.Contain("--channel"));
            Assert.That(output, Does.Contain("--queue-manager"));
            Assert.That(output, Does.Contain("--user"));
            Assert.That(output, Does.Contain("--password"));
        });
    }

    [Test]
    public async Task Endpoint_subscribe_help_shows_topic_prefix_option()
    {
        var (output, _, _) = await Execute("endpoint subscribe --help").ConfigureAwait(false);

        Assert.That(output, Does.Contain("--topic-prefix"));
    }

    [Test]
    public async Task Endpoint_subscribe_help_shows_assembly_option()
    {
        var (output, _, _) = await Execute("endpoint subscribe --help").ConfigureAwait(false);

        Assert.That(output, Does.Contain("--assembly"));
    }

    [Test]
    public async Task Endpoint_unsubscribe_help_shows_assembly_option()
    {
        var (output, _, _) = await Execute("endpoint unsubscribe --help").ConfigureAwait(false);

        Assert.That(output, Does.Contain("--assembly"));
    }

#pragma warning disable PS0018
    static async Task<(string output, string error, int exitCode)> Execute(string command)
#pragma warning restore PS0018
    {
        var assemblyCsproj = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "..", "..", "..", "..",
            "CommandLine", "NServiceBus.Transport.IBMMQ.CommandLine.csproj"
        );

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{assemblyCsproj}\" -- {command}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        return (output.Trim(), error.Trim(), process.ExitCode);
    }
}
