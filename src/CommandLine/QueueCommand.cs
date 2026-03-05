namespace NServiceBus.Transport.IBMMQ.CommandLine;

using McMaster.Extensions.CommandLineUtils;

static class QueueCommand
{
    public static void Register(CommandLineApplication app, ConnectionOptions connectionOptions)
    {
        app.Command("queue", cmd =>
        {
            cmd.Description = "Manage queues";
            cmd.HelpOption();

            RegisterCreate(cmd, connectionOptions);
            RegisterDelete(cmd, connectionOptions);

            cmd.OnExecute(() => cmd.ShowHelp());
        });
    }

    static void RegisterCreate(CommandLineApplication parent, ConnectionOptions connectionOptions)
    {
        parent.Command("create", cmd =>
        {
            cmd.Description = "Create a queue";
            cmd.HelpOption();

            var name = cmd.Argument("name", "Name of the queue").IsRequired();
            var maxDepth = cmd.Option<int>("--max-depth", "Maximum queue depth (default: 5000)", CommandOptionType.SingleValue);

            cmd.OnExecute(() =>
            {
                using var connection = connectionOptions.Connect();
                var depth = maxDepth.HasValue() ? maxDepth.ParsedValue : 5000;

                Queue.Create(connection, name.Value!, depth);
            });
        });
    }

    static void RegisterDelete(CommandLineApplication parent, ConnectionOptions connectionOptions)
    {
        parent.Command("delete", cmd =>
        {
            cmd.Description = "Delete a queue";
            cmd.HelpOption();

            var name = cmd.Argument("name", "Name of the queue").IsRequired();

            cmd.OnExecute(() =>
            {
                using var connection = connectionOptions.Connect();
                Queue.Delete(connection, name.Value!);
            });
        });
    }
}
