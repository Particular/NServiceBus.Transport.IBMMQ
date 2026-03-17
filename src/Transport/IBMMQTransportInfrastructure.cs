namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
using Microsoft.Extensions.DependencyInjection;
using Logging;

sealed class IBMMQTransportInfrastructure : TransportInfrastructure, IAsyncDisposable
{
    const int DefaultDestinationCacheCapacity = 100;

    readonly ILog log;
    readonly ServiceProvider serviceProvider;
    int _disposed;

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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

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

        // Cache created topics across all connections to avoid redundant admin connection
        // creation. Topic creation is idempotent; the cache prevents repeated attempts for
        // topics that have already been created successfully. GetOrAdd ensures the entry is
        // only stored on success — if CreateTopic throws, the next call will retry.
        var createdTopics = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
        CreateTopic createTopic = (topicName, topicString) =>
            createdTopics.GetOrAdd(topicName, _ =>
            {
                using var admin = createAdmin();
                admin.CreateTopic(topicName, topicString);
                return 0;
            });

        var pumpSettings = new MessagePumpSettings(messageWaitInterval);

        MqConnection createDataPathConnection() => new(
            LogManager.GetLogger<MqConnection>(),
            new MQQueueManager(queueManagerName, connectionProperties),
            resourceNameFormatter,
            createTopic,
            DefaultDestinationCacheCapacity);

        services
            .AddSingleton(topology)
            .AddSingleton<MqPropertyNameEncoder>()
            .AddSingleton<IBMMQMessageConverter>()
            .AddSingleton(new MqConnectionPool(LogManager.GetLogger<MqConnectionPool>(), createDataPathConnection, Environment.ProcessorCount))
            .AddSingleton<IMessageDispatcher>(sp =>
                new MessageDispatcher(
                    sp.GetRequiredService<MqConnectionPool>(),
                    sp.GetRequiredService<TopicTopology>(),
                    sp.GetRequiredService<IBMMQMessageConverter>()))
            .AddSingleton(createAdmin)
            .AddSingleton(pumpSettings)
            .AddSingleton<CreateMessagePumpWorker>(sp =>
                (queueName, onMessage, onError, workerIndex) => new MessagePumpWorker(
                    LogManager.GetLogger<MessagePumpWorker>(),
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetRequiredService<MessagePumpSettings>(),
                    criticalError,
                    queueName, onMessage, onError, workerIndex
                ));

        if (transactionMode == TransportTransactionMode.SendsAtomicWithReceive)
        {
            services.AddSingleton<IFailureInfoStorage>(new InMemoryFailureInfoStorage());
        }

        services
            .AddScoped(_ => createDataPathConnection())
            .AddScoped<CreateReceiveStrategy>(sp =>
                ctx =>
                {
                    var conn = sp.GetRequiredService<MqConnection>();
                    var converter = sp.GetRequiredService<IBMMQMessageConverter>();
                    var strategyLog = LogManager.GetLogger<ReceiveStrategy>();
                    return transactionMode switch
                    {
                        TransportTransactionMode.None =>
                            new NoTransactionReceiveStrategy(strategyLog, conn, converter, ctx),
                        TransportTransactionMode.ReceiveOnly =>
                            new ReceiveOnlyReceiveStrategy(strategyLog, conn, converter, ctx),
                        TransportTransactionMode.SendsAtomicWithReceive =>
                            new AtomicReceiveStrategy(strategyLog, conn, converter, sp.GetRequiredService<IFailureInfoStorage>(), ctx),
                        TransportTransactionMode.TransactionScope =>
                            throw new NotSupportedException("TransactionScope is not supported"),
                        _ => throw new ArgumentOutOfRangeException(nameof(transactionMode), transactionMode, "Unsupported transaction mode")
                    };
                }
            );

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
                    var workerFactory = sp.GetRequiredService<CreateMessagePumpWorker>();
                    var subMgr = sp.GetRequiredKeyedService<ISubscriptionManager>(rs.Id);
                    return new IBMMQMessageReceiver(
                        LogManager.GetLogger<IBMMQMessageReceiver>(),
                        workerFactory, subMgr, rs, resourceNameFormatter);
                });
        }
    }
}
