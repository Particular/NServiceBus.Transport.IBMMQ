namespace NServiceBus.Transport.IbmMq.CommandLine;

using McMaster.Extensions.CommandLineUtils;

class Program
{
    static int Main(string[] args)
    {
        var app = new CommandLineApplication
        {
            Name = "ibmmq-transport"
        };

        var host = app.Option("--host", "IBM MQ host (env: IBMMQ_HOST, default: localhost)", CommandOptionType.SingleValue);
        var port = app.Option<int>("--port", "IBM MQ port (env: IBMMQ_PORT, default: 1414)", CommandOptionType.SingleValue);
        var channel = app.Option("--channel", "IBM MQ channel (env: IBMMQ_CHANNEL, default: DEV.ADMIN.SVRCONN)", CommandOptionType.SingleValue);
        var queueManager = app.Option("--queue-manager", "Queue manager name (env: IBMMQ_QUEUE_MANAGER)", CommandOptionType.SingleValue);
        var user = app.Option("--user", "User ID (env: IBMMQ_USER)", CommandOptionType.SingleValue);
        var password = app.Option("--password", "Password (env: IBMMQ_PASSWORD)", CommandOptionType.SingleValue);

        app.HelpOption();

        app.Command("endpoint", endpointCommand =>
        {
            endpointCommand.HelpOption();
            endpointCommand.Description = "Manage endpoint infrastructure";

            endpointCommand.Command("create", createCommand =>
            {
                createCommand.HelpOption();
                createCommand.Description = "Create endpoint infrastructure (queue)";

                var name = createCommand.Argument("name", "Name of the endpoint").IsRequired();

                createCommand.OnExecuteAsync(async ct =>
                {
                    var runner = CommandRunner.Create(host, port, channel, queueManager, user, password);

                    await Endpoint.Create(runner, name.Value!).ConfigureAwait(false);
                });
            });

            endpointCommand.Command("subscribe", subscribeCommand =>
            {
                subscribeCommand.HelpOption();
                subscribeCommand.Description = "Subscribe endpoint to an event type";

                var name = subscribeCommand.Argument("name", "Name of the endpoint").IsRequired();
                var eventType = subscribeCommand.Argument("event-type", "Fully qualified .NET type name of the event").IsRequired();
                var topicPrefix = subscribeCommand.Option("--topic-prefix", "Topic name prefix (default: DEV)", CommandOptionType.SingleValue);

                subscribeCommand.OnExecuteAsync(async ct =>
                {
                    var runner = CommandRunner.Create(host, port, channel, queueManager, user, password);
                    var prefix = topicPrefix.Value() ?? "DEV";

                    await Endpoint.Subscribe(runner, name.Value!, eventType.Value!, prefix).ConfigureAwait(false);
                });
            });

            endpointCommand.Command("unsubscribe", unsubscribeCommand =>
            {
                unsubscribeCommand.HelpOption();
                unsubscribeCommand.Description = "Unsubscribe endpoint from an event type";

                var name = unsubscribeCommand.Argument("name", "Name of the endpoint").IsRequired();
                var eventType = unsubscribeCommand.Argument("event-type", "Fully qualified .NET type name of the event").IsRequired();
                var topicPrefix = unsubscribeCommand.Option("--topic-prefix", "Topic name prefix (default: DEV)", CommandOptionType.SingleValue);

                unsubscribeCommand.OnExecuteAsync(async ct =>
                {
                    var runner = CommandRunner.Create(host, port, channel, queueManager, user, password);
                    var prefix = topicPrefix.Value() ?? "DEV";

                    await Endpoint.Unsubscribe(runner, name.Value!, eventType.Value!, prefix).ConfigureAwait(false);
                });
            });

            endpointCommand.OnExecute(() => endpointCommand.ShowHelp());
        });

        app.Command("queue", queueCommand =>
        {
            queueCommand.HelpOption();
            queueCommand.Description = "Manage queues";

            queueCommand.Command("create", createCommand =>
            {
                createCommand.HelpOption();
                createCommand.Description = "Create a queue";

                var name = createCommand.Argument("name", "Name of the queue").IsRequired();
                var maxDepth = createCommand.Option<int>("--max-depth", "Maximum queue depth (default: 5000)", CommandOptionType.SingleValue);

                createCommand.OnExecuteAsync(async ct =>
                {
                    var runner = CommandRunner.Create(host, port, channel, queueManager, user, password);
                    var depth = maxDepth.HasValue() ? maxDepth.ParsedValue : 5000;

                    await Queue.Create(runner, name.Value!, depth).ConfigureAwait(false);
                });
            });

            queueCommand.Command("delete", deleteCommand =>
            {
                deleteCommand.HelpOption();
                deleteCommand.Description = "Delete a queue";

                var name = deleteCommand.Argument("name", "Name of the queue").IsRequired();

                deleteCommand.OnExecuteAsync(async ct =>
                {
                    var runner = CommandRunner.Create(host, port, channel, queueManager, user, password);

                    await Queue.Delete(runner, name.Value!).ConfigureAwait(false);
                });
            });

            queueCommand.OnExecute(() => queueCommand.ShowHelp());
        });

        app.OnExecute(() => app.ShowHelp());

        return app.Execute(args);
    }
}
