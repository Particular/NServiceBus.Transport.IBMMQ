namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
using Microsoft.Extensions.DependencyInjection;
using Logging;

sealed class IBMMQTransportInfrastructure : TransportInfrastructure, IAsyncDisposable
{
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
        ConfigureServices(services, options, connectionConfiguration, receiverSettings, transactionMode, criticalError);
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
        await DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        log.Debug("Disposing");
        await serviceProvider.DisposeAsync().ConfigureAwait(false);
    }

    static void ConfigureServices(
        IServiceCollection services,
        IBMMQTransportOptions options,
        ConnectionConfiguration connectionConfiguration,
        ReceiveSettings[] receiverSettings,
        TransportTransactionMode transactionMode,
        Action<string, Exception, CancellationToken> criticalError)
    {
        var queueManagerName = connectionConfiguration.QueueManagerName;
        var connectionProperties = connectionConfiguration.ConnectionProperties;
        var messageWaitInterval = connectionConfiguration.MessageWaitInterval;
        SanitizeResourceName resourceNameFormatter = options.ResourceNameSanitizer;
        var topology = options.Topology;
        topology.Naming = options.TopicNaming;

        services
            .AddSingleton(topology)
            .AddSingleton<CreateQueueManager>(() => new MQQueueManager(queueManagerName, connectionProperties))
            .AddSingleton(new MessagePumpSettings(messageWaitInterval, transactionMode))
            .AddScoped(sp => new MessagePumpWorker(
                LogManager.GetLogger<MessagePumpWorker>(),
                sp.GetRequiredService<MessagePumpSettings>(),
                sp.GetRequiredService<CreateQueueManager>(),
                criticalError
            ))
            .AddSingleton<CreateQueueManagerFacade>(qm =>
                new MqQueueManagerFacade(qm, resourceNameFormatter))
            .AddSingleton(new MqConnectionPool(
                () => new MQQueueManager(queueManagerName, connectionProperties),
                qm => new MqQueueManagerFacade(qm, resourceNameFormatter),
                Environment.ProcessorCount))
            .AddSingleton<IMessageDispatcher>(sp =>
            {
                var pool = sp.GetRequiredService<MqConnectionPool>();
                var createFacade = sp.GetRequiredService<CreateQueueManagerFacade>();
                var topo = sp.GetRequiredService<TopicTopology>();

                return transactionMode switch
                {
                    TransportTransactionMode.None => new MessageDispatcher(pool, topo),
                    TransportTransactionMode.ReceiveOnly => new MessageDispatcher(pool, topo),
                    TransportTransactionMode.SendsAtomicWithReceive => new AtomicMessageDispatcher(pool, topo, createFacade),
                    TransportTransactionMode.TransactionScope => throw new NotSupportedException("TransactionScope is not supported"),
                    _ => throw new ArgumentOutOfRangeException(nameof(transactionMode), transactionMode, "Unsupported transaction mode")
                };
            });

        foreach (var rs in receiverSettings)
        {
            services
                .AddKeyedSingleton<ISubscriptionManager>(rs.Id, (sp, _) =>
                {
                    var createFacade = sp.GetRequiredService<CreateQueueManagerFacade>();
                    var createConnection = sp.GetRequiredService<CreateQueueManager>();
                    var topo = sp.GetRequiredService<TopicTopology>();
                    return new IBMMQSubscriptionManager(
                        LogManager.GetLogger<IBMMQSubscriptionManager>(),
                        topo, createFacade, createConnection, rs.ReceiveAddress.BaseAddress);
                })
                .AddSingleton<IMessageReceiver>(sp =>
                {
                    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
                    var subMgr = sp.GetRequiredKeyedService<ISubscriptionManager>(rs.Id);
                    var pSettings = sp.GetRequiredService<MessagePumpSettings>();
                    return new IBMMQMessageReceiver(
                        LogManager.GetLogger<IBMMQMessageReceiver>(),
                        scopeFactory, subMgr, rs, pSettings, resourceNameFormatter);
                });
        }
    }
}
