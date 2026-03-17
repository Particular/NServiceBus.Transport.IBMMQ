namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
using Microsoft.Extensions.DependencyInjection;
using Logging;

sealed class IBMMQTransportInfrastructure : TransportInfrastructure, IAsyncDisposable
{
    const int DefaultDestinationCacheCapacity = 100;

    readonly ILog log;
    readonly ServiceProvider serviceProvider;
    bool _disposed;

    public IBMMQTransportInfrastructure(
        ILog log,
        IBMMQTransportOptions options,
        ConnectionConfiguration connectionConfiguration,
        ReceiveSettings[] receiverSettings,
        TransportTransactionMode transactionMode,
        Action<string, Exception, CancellationToken> criticalError
    )
    {
        this.log = log;
        ArgumentNullException.ThrowIfNull(connectionConfiguration);
        ArgumentNullException.ThrowIfNull(receiverSettings);

        var services = new ServiceCollection();
        ConfigureServices(
            services,
            options,
            connectionConfiguration,
            receiverSettings,
            transactionMode,
            criticalError
        );

        serviceProvider = services.BuildServiceProvider();

        Dispatcher = serviceProvider.GetRequiredService<IMessageDispatcher>();
        Receivers = serviceProvider.GetServices<IMessageReceiver>()
            .ToDictionary(r => r.Id);
    }

    public override string ToTransportAddress(QueueAddress address) =>
        IBMMQMessageReceiver.ToTransportAddress(address);

    public override async Task Shutdown(CancellationToken cancellationToken = default)
    {
        log.Debug("Shutdown");
        await DisposeAsync()
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        log.Debug("Disposing");
        await serviceProvider.DisposeAsync()
            .ConfigureAwait(false);
    }

    static void ConfigureServices(
        IServiceCollection services,
        IBMMQTransportOptions options,
        ConnectionConfiguration connectionConfiguration,
        ReceiveSettings[] receiverSettings,
        TransportTransactionMode transactionMode,
        Action<string, Exception, CancellationToken> criticalError
    )
    {
        var queueManagerName = connectionConfiguration.QueueManagerName;
        var connectionProperties = connectionConfiguration.ConnectionProperties;
        var messageWaitInterval = connectionConfiguration.MessageWaitInterval;
        SanitizeResourceName resourceNameFormatter = options.ResourceNameSanitizer;
        var topology = options.Topology;
        topology.Naming = options.TopicNaming;

        CreateMqAdminConnection createAdmin = () =>
            new MqAdminConnection(new MQQueueManager(queueManagerName, connectionProperties), resourceNameFormatter);

        CreateTopic createTopic = (topicName, topicString) =>
        {
            using var admin = createAdmin();
            admin.CreateTopic(topicName, topicString);
        };

        services
            .AddSingleton(topology)
            .AddSingleton<MqPropertyNameEncoder>()
            .AddSingleton<IBMMQMessageConverter>()
            .AddSingleton(new MqConnectionPool(
                () => new MqConnection(
                    LogManager.GetLogger<MqConnection>(),
                    new MQQueueManager(queueManagerName, connectionProperties),
                    resourceNameFormatter,
                    createTopic,
                    DefaultDestinationCacheCapacity),
                Environment.ProcessorCount))
            .AddSingleton<IMessageDispatcher>(sp =>
                new MessageDispatcher(
                    sp.GetRequiredService<MqConnectionPool>(),
                    sp.GetRequiredService<TopicTopology>(),
                    sp.GetRequiredService<IBMMQMessageConverter>()))
            .AddSingleton(createAdmin)
            .AddSingleton(new MessagePumpSettings(messageWaitInterval));

        if (transactionMode == TransportTransactionMode.SendsAtomicWithReceive)
        {
            services.AddSingleton<IFailureInfoStorage>(new InMemoryFailureInfoStorage());
        }

        services
            .AddScoped(_ => new MqConnection(
                LogManager.GetLogger<MqConnection>(),
                new MQQueueManager(queueManagerName, connectionProperties),
                resourceNameFormatter,
                createTopic,
                DefaultDestinationCacheCapacity))
            .AddScoped<ReceiveStrategy>(sp =>
            {
                var conn = sp.GetRequiredService<MqConnection>();
                var converter = sp.GetRequiredService<IBMMQMessageConverter>();
                var strategyLog = LogManager.GetLogger<ReceiveStrategy>();
                return transactionMode switch
                {
                    TransportTransactionMode.None =>
                        new NoTransactionReceiveStrategy(strategyLog, conn, converter),
                    TransportTransactionMode.ReceiveOnly =>
                        new ReceiveOnlyReceiveStrategy(strategyLog, conn, converter),
                    TransportTransactionMode.SendsAtomicWithReceive =>
                        new AtomicReceiveStrategy(strategyLog, conn, converter, sp.GetRequiredService<IFailureInfoStorage>()),
                    TransportTransactionMode.TransactionScope =>
                        throw new NotSupportedException("TransactionScope is not supported"),
                    _ => throw new ArgumentOutOfRangeException(nameof(transactionMode), transactionMode, "Unsupported transaction mode")
                };
            })
            .AddScoped(sp => new MessagePumpWorker(
                LogManager.GetLogger<MessagePumpWorker>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<MessagePumpSettings>(),
                criticalError
            ));

        foreach (var rs in receiverSettings)
        {
            services
                .AddKeyedSingleton<ISubscriptionManager>(rs.Id, (sp, _) =>
                {
                    var createAdmin = sp.GetRequiredService<CreateMqAdminConnection>();
                    var topo = sp.GetRequiredService<TopicTopology>();
                    return new IBMMQSubscriptionManager(
                        LogManager.GetLogger<IBMMQSubscriptionManager>(),
                        topo, createAdmin, rs.ReceiveAddress.BaseAddress);
                })
                .AddSingleton<IMessageReceiver>(sp =>
                {
                    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
                    var subMgr = sp.GetRequiredKeyedService<ISubscriptionManager>(rs.Id);
                    return new IBMMQMessageReceiver(
                        LogManager.GetLogger<IBMMQMessageReceiver>(),
                        scopeFactory, subMgr, rs, resourceNameFormatter);
                });
        }
    }
}
