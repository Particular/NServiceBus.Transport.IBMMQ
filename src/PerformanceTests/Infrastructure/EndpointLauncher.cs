namespace NServiceBus.Transport.IbmMq.PerformanceTests.Infrastructure;

using NServiceBus.Logging;
using NServiceBus.Transport.IbmMq;
using NServiceBus.Transport.IbmMq.PerformanceTests.Handlers;

record EndpointSpec
{
    public required string Name { get; init; }
    public required TransportTransactionMode TransactionMode { get; init; }
    public bool SendOnly { get; init; }
    public string? ErrorQueue { get; init; }
    public int ImmediateRetries { get; init; } = 5;
    public int DelayedRetries { get; init; } = 0;
    public Type[] HandlerTypes { get; init; } = [];
    public Dictionary<Type, string>? Routing { get; init; }
}

static class EndpointLauncher
{
    static readonly Type[] AllHandlerTypes =
    [
        typeof(ReceiveThroughputHandler),
        typeof(ReceiveAndSendHandler),
        typeof(FailureHandler),
        typeof(SendLocalHandler),
        typeof(PublishHandler)
    ];

    public static async Task<IEndpointInstance[]> StartMultiple(EndpointSpec spec, int instanceCount, CancellationToken cancellationToken = default)
    {
        var instances = new IEndpointInstance[instanceCount];
        for (int i = 0; i < instanceCount; i++)
        {
            instances[i] = await Start(spec, $"{spec.Name}-{i + 1}", cancellationToken).ConfigureAwait(false);
        }
        return instances;
    }

    public static async Task StopAll(IEndpointInstance[] instances, CancellationToken cancellationToken = default)
    {
        foreach (var instance in instances)
        {
            await instance.Stop(cancellationToken).ConfigureAwait(false);
        }
    }

    static async Task<IEndpointInstance> Start(EndpointSpec spec, string instanceName, CancellationToken cancellationToken)
    {
        var (host, user, password, port) = MqConnectionFactory.ParseConnectionDetails();

        var config = new EndpointConfiguration(spec.Name);

        config.UniquelyIdentifyRunningInstance()
            .UsingNames(
                instanceName: instanceName,
                hostName: Environment.MachineName);

        var transport = new IbmMqTransport(options =>
        {
            options.Host = host;
            options.User = user;
            options.Password = password;
            options.Port = port;
            options.QueueManagerName = "QM1";
            options.MessageWaitInterval = TimeSpan.FromMilliseconds(500);
            options.ResourceNameSanitizer = MqConnectionFactory.FormatQueueName;
            options.TopicNaming = new ShortenedTopicNaming();
        })
        {
            TransportTransactionMode = spec.TransactionMode
        };

        config.UseSerialization<SystemJsonSerializer>();

        var transportConfig = config.UseTransport(transport);

        if (spec.Routing != null)
        {
            foreach (var (messageType, destination) in spec.Routing)
            {
                transportConfig.RouteToEndpoint(messageType, destination);
            }
        }

        if (spec.SendOnly)
        {
            config.SendOnly();
        }

        if (spec.ErrorQueue != null)
        {
            config.SendFailedMessagesTo(spec.ErrorQueue);
        }

        var immediateRetries = spec.TransactionMode == TransportTransactionMode.None ? 0 : spec.ImmediateRetries;
        config.Recoverability().Immediate(i => i.NumberOfRetries(immediateRetries));
        config.Recoverability().Delayed(d => d.NumberOfRetries(spec.DelayedRetries));

        config.EnableInstallers();

        // Exclude all handler types except the ones specified
        var excludedTypes = AllHandlerTypes.Except(spec.HandlerTypes).ToArray();
        if (excludedTypes.Length > 0)
        {
            config.AssemblyScanner().ExcludeTypes(excludedTypes);
        }

        return await Endpoint.Start(config, cancellationToken).ConfigureAwait(false);
    }
}
