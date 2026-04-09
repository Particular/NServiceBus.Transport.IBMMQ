namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
using Logging;
using Microsoft.Extensions.DependencyInjection;

sealed class IBMMQTransportInfrastructure : TransportInfrastructure, IAsyncDisposable
{
    const int DefaultDestinationCacheCapacity = 100;

    readonly ILog log;
    readonly ServiceProvider serviceProvider;
    int _disposed;

    public IBMMQTransportInfrastructure(
        ILog log,
        IBMMQTransport transport,
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
            transport,
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
        IBMMQTransport transport,
        ConnectionConfiguration connectionConfiguration,
        ReceiveSettings[] receiverSettings,
        TransportTransactionMode transactionMode,
        Action<string, Exception, CancellationToken> criticalError
    )
    {
        var queueManagerName = connectionConfiguration.QueueManagerName;
        var connectionProperties = connectionConfiguration.ConnectionProperties;
        var messageWaitInterval = connectionConfiguration.MessageWaitInterval;
        SanitizeResourceName resourceNameFormatter = transport.ResourceNameSanitizer;
        var characterSet = transport.CharacterSet;

        MqAdminConnection CreateAdmin() => new(new MQQueueManager(queueManagerName, connectionProperties), resourceNameFormatter);

        // Cache created topics across all connections to avoid redundant admin connection
        // creation. Topic creation is idempotent; the cache prevents repeated attempts for
        // topics that have already been created successfully. GetOrAdd ensures the entry is
        // only stored on success — if CreateTopic throws, the next call will retry.
        var createdTopics = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();

        void CreateTopic(string topicName, string topicString) =>
            createdTopics.GetOrAdd(topicName, _ =>
            {
                using var admin = CreateAdmin();
                admin.CreateTopic(topicName, topicString);
                return 0;
            });

        var pumpSettings = new MessagePumpSettings(messageWaitInterval);

        MqConnection CreateDataPathConnection() => new(
            LogManager.GetLogger<MqConnection>(),
            new MQQueueManager(queueManagerName, connectionProperties),
            resourceNameFormatter,
            CreateTopic,
            DefaultDestinationCacheCapacity);

        var circuitBreakerTimeout = transport.TimeToWaitBeforeTriggeringCircuitBreaker;

        services
            .AddSingleton((TopicTopology)transport.Topology)
            .AddSingleton<MqPropertyNameEncoder>()
            .AddSingleton(sp => new IBMMQMessageConverter(sp.GetRequiredService<MqPropertyNameEncoder>(), characterSet))
            .AddSingleton(new MqConnectionPool(LogManager.GetLogger<MqConnectionPool>(), CreateDataPathConnection, Environment.ProcessorCount))
            .AddSingleton<IMessageDispatcher>(sp =>
                new MessageDispatcher(
                    sp.GetRequiredService<MqConnectionPool>(),
                    sp.GetRequiredService<TopicTopology>(),
                    sp.GetRequiredService<IBMMQMessageConverter>()))
            .AddSingleton<CreateMqAdminConnection>(CreateAdmin)
            .AddSingleton(pumpSettings);

        if (transactionMode == TransportTransactionMode.SendsAtomicWithReceive)
        {
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IFailureInfoStorage, InMemoryFailureInfoStorage>();
        }

        services
            .AddScoped(_ => CreateDataPathConnection())
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
            var receiveAddress = resourceNameFormatter(IBMMQMessageReceiver.ToTransportAddress(rs.ReceiveAddress));

            services
                .AddKeyedSingleton<ISubscriptionManager>(rs.Id, (sp, _) =>
                {
                    var createAdmin = sp.GetRequiredService<CreateMqAdminConnection>();
                    var topo = sp.GetRequiredService<TopicTopology>();
                    return new IBMMQSubscriptionManager(
                        LogManager.GetLogger<IBMMQSubscriptionManager>(),
                        topo,
                        createAdmin,
                        rs.ReceiveAddress.BaseAddress
                        );
                })
                .AddKeyedSingleton(rs.Id, (_, _) =>
                    new RepeatedFailuresOverTimeCircuitBreaker(
                        $"'{receiveAddress}'",
                        circuitBreakerTimeout,
                        ex => criticalError($"Failed to receive from {receiveAddress}", ex, CancellationToken.None)))
                .AddSingleton<IMessageReceiver>(sp =>
                {
                    var cb = sp.GetRequiredKeyedService<RepeatedFailuresOverTimeCircuitBreaker>(rs.Id);
                    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
                    var settings = sp.GetRequiredService<MessagePumpSettings>();

                    MessagePumpWorker WorkerFactory(string queueName, OnMessage onMessage, OnError onError, int workerIndex) =>
                        new MessagePumpWorker(
                            LogManager.GetLogger<MessagePumpWorker>(),
                            scopeFactory, settings, criticalError, cb,
                            queueName, onMessage, onError, workerIndex);

                    var subMgr = sp.GetRequiredKeyedService<ISubscriptionManager>(rs.Id);
                    return new IBMMQMessageReceiver(
                        LogManager.GetLogger<IBMMQMessageReceiver>(),
                        WorkerFactory, subMgr, rs, resourceNameFormatter);
                });
        }
    }
}
