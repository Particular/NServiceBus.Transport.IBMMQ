namespace NServiceBus.Transport.IBMMQ.CommandLine;

using McMaster.Extensions.CommandLineUtils;

static class EndpointCommand
{
    public static void Register(CommandLineApplication app, ConnectionOptions connectionOptions)
    {
        app.Command("endpoint", cmd =>
        {
            cmd.Description = "Manage endpoint infrastructure";
            cmd.HelpOption();

            RegisterCreate(cmd, connectionOptions);
            RegisterSubscribe(cmd, connectionOptions);
            RegisterUnsubscribe(cmd, connectionOptions);

            cmd.OnExecute(() => cmd.ShowHelp());
        });
    }

    static void RegisterCreate(CommandLineApplication parent, ConnectionOptions connectionOptions)
    {
        parent.Command("create", cmd =>
        {
            cmd.Description = "Create endpoint infrastructure (queue)";
            cmd.HelpOption();

            var name = cmd.Argument("name", "Name of the endpoint").IsRequired();

            cmd.OnExecute(() =>
            {
                using var connection = connectionOptions.Connect();
                Queue.Create(connection, name.Value!);
            });
        });
    }

    static void RegisterSubscribe(CommandLineApplication parent, ConnectionOptions connectionOptions)
    {
        parent.Command("subscribe", cmd =>
        {
            cmd.Description = "Subscribe endpoint to an event type";
            cmd.HelpOption();

            var name = cmd.Argument("name", "Name of the endpoint").IsRequired();
            var eventType = cmd.Argument("event-type", "Fully qualified .NET type name of the event").IsRequired();
            var topicPrefix = cmd.Option("--topic-prefix", "Topic name prefix (default: DEV)", CommandOptionType.SingleValue);
            var assemblyPath = cmd.Option("--assembly", "Path to assembly containing the event type (enables polymorphic subscriptions)", CommandOptionType.SingleValue);

            cmd.OnExecute(() =>
            {
                using var connection = connectionOptions.Connect();

                var prefix = topicPrefix.Value() ?? "DEV";
                var typeNames = ResolveEventTypeNames(eventType.Value!, assemblyPath.Value());

                foreach (var typeName in typeNames)
                {
                    var topicName = TopicNaming.GenerateTopicName(typeName, prefix);
                    var topicString = TopicNaming.GenerateTopicString(typeName, prefix);
                    var subscriptionName = TopicNaming.GenerateSubscriptionName(name.Value!, topicString);

                    Topic.Create(connection, topicName, topicString);
                    Subscription.Create(connection, topicString, subscriptionName, name.Value!);
                }
            });
        });
    }

    static void RegisterUnsubscribe(CommandLineApplication parent, ConnectionOptions connectionOptions)
    {
        parent.Command("unsubscribe", cmd =>
        {
            cmd.Description = "Unsubscribe endpoint from an event type";
            cmd.HelpOption();

            var name = cmd.Argument("name", "Name of the endpoint").IsRequired();
            var eventType = cmd.Argument("event-type", "Fully qualified .NET type name of the event").IsRequired();
            var topicPrefix = cmd.Option("--topic-prefix", "Topic name prefix (default: DEV)", CommandOptionType.SingleValue);
            var assemblyPath = cmd.Option("--assembly", "Path to assembly containing the event type (enables polymorphic unsubscriptions)", CommandOptionType.SingleValue);

            cmd.OnExecute(() =>
            {
                using var connection = connectionOptions.Connect();

                var prefix = topicPrefix.Value() ?? "DEV";
                var typeNames = ResolveEventTypeNames(eventType.Value!, assemblyPath.Value());

                foreach (var typeName in typeNames)
                {
                    var topicString = TopicNaming.GenerateTopicString(typeName, prefix);
                    var subscriptionName = TopicNaming.GenerateSubscriptionName(name.Value!, topicString);

                    Subscription.Delete(connection, subscriptionName);
                }
            });
        });
    }

    static IReadOnlyList<string> ResolveEventTypeNames(string eventTypeName, string? assemblyPath)
    {
        if (assemblyPath is null)
        {
            return [eventTypeName];
        }

        return EventTypeResolver.Resolve(eventTypeName, assemblyPath);
    }
}
