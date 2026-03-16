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
            .AddSingleton<MqPropertyNameEncoder>()
            .AddSingleton<IBMMQMessageConverter>()
            .AddSingleton(new MqConnectionPool(
                () => new MqConnection(
                    new MQQueueManager(queueManagerName, connectionProperties),
                    resourceNameFormatter, 100),
                Environment.ProcessorCount))
            .AddSingleton<IMessageDispatcher>(sp =>
                new MessageDispatcher(
                    sp.GetRequiredService<MqConnectionPool>(),
                    sp.GetRequiredService<TopicTopology>(),
                    sp.GetRequiredService<IBMMQMessageConverter>()))
            .AddSingleton<CreateMqAdminConnection>(() =>
                new MqAdminConnection(new MQQueueManager(queueManagerName, connectionProperties), resourceNameFormatter))
            .AddSingleton(new MessagePumpSettings(messageWaitInterval, transactionMode))
            .AddScoped(_ => new MqConnection(
                new MQQueueManager(queueManagerName, connectionProperties),
                resourceNameFormatter, 100))
            .AddScoped<ReceiveStrategy>(sp =>
            {
                var conn = sp.GetRequiredService<MqConnection>();
                var converter = sp.GetRequiredService<IBMMQMessageConverter>();
                var strategyLog = LogManager.GetLogger<ReceiveStrategy>();
                return transactionMode switch
                {
                    TransportTransactionMode.None =>
                        new NoTransactionReceiveStrategy(conn, converter, strategyLog),
                    TransportTransactionMode.ReceiveOnly =>
                        new ReceiveOnlyReceiveStrategy(conn, converter, strategyLog),
                    TransportTransactionMode.SendsAtomicWithReceive =>
                        new AtomicReceiveStrategy(conn, converter, strategyLog),
                    TransportTransactionMode.TransactionScope =>
                        throw new NotSupportedException("TransactionScope is not supported"),
                    _ => throw new ArgumentOutOfRangeException(nameof(transactionMode), transactionMode, "Unsupported transaction mode")
                };
            });

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
                    var pSettings = sp.GetRequiredService<MessagePumpSettings>();
                    return new IBMMQMessageReceiver(
                        LogManager.GetLogger<IBMMQMessageReceiver>(),
                        scopeFactory, subMgr, rs, pSettings, resourceNameFormatter, criticalError);
                });
        }
    }
}
