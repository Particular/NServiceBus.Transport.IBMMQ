namespace NServiceBus.Transport.IbmMq;

using IBM.WMQ;
using Microsoft.Extensions.DependencyInjection;
using Logging;

sealed class IbmMqTransportInfrastructure : TransportInfrastructure, IAsyncDisposable
{
    readonly ILog log;
    readonly ServiceProvider serviceProvider;
    bool _disposed;

    public IbmMqTransportInfrastructure(
        ILog log,
        IbmMqTransportOptions options,
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
        IbmMqMessageReceiver.ToTransportAddress(address);

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
        IbmMqTransportOptions options,
        ConnectionConfiguration connectionConfiguration,
        ReceiveSettings[] receiverSettings,
        TransportTransactionMode transactionMode,
        Action<string, Exception, CancellationToken> criticalError)
    {
        var queueManagerName = connectionConfiguration.QueueManagerName;
        var connectionProperties = connectionConfiguration.ConnectionProperties;
        var messageWaitInterval = connectionConfiguration.MessageWaitInterval;
        SanitizeResourceName resourceNameFormatter = options.ResourceNameSanitizer;

        services
            .AddSingleton<CreateQueueManager>(() => new MQQueueManager(queueManagerName, connectionProperties))
            .AddSingleton(new MessagePumpSettings(messageWaitInterval, transactionMode))
            .AddScoped(sp => new MessagePumpWorker(
                LogManager.GetLogger<MessagePumpWorker>(),
                sp.GetRequiredService<MessagePumpSettings>(),
                sp.GetRequiredService<CreateQueueManager>(),
                criticalError
            ))
            .AddSingleton<CreateQueueManagerFacade>(qm =>
                new MqQueueManagerFacade(qm, resourceNameFormatter, options.TopicPrefix))
            .AddSingleton(new MQQueueManager(queueManagerName, connectionProperties))
            .AddSingleton<IMessageDispatcher>(sp =>
            {
                var sendConnection = sp.GetRequiredService<MQQueueManager>();
                var createFacade = sp.GetRequiredService<CreateQueueManagerFacade>();
                var sendFacade = createFacade(sendConnection);

                return transactionMode switch
                {
                    TransportTransactionMode.None => new MessageDispatcher(sendFacade),
                    TransportTransactionMode.ReceiveOnly => new MessageDispatcher(sendFacade),
                    TransportTransactionMode.SendsAtomicWithReceive => new AtomicMessageDispatcher(sendFacade, createFacade),
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
                    return new IbmMqSubscriptionManager(
                        LogManager.GetLogger<IbmMqSubscriptionManager>(),
                        createFacade, createConnection, rs.ReceiveAddress.BaseAddress);
                })
                .AddSingleton<IMessageReceiver>(sp =>
                {
                    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
                    var subMgr = sp.GetRequiredKeyedService<ISubscriptionManager>(rs.Id);
                    var pSettings = sp.GetRequiredService<MessagePumpSettings>();
                    return new IbmMqMessageReceiver(
                        LogManager.GetLogger<IbmMqMessageReceiver>(),
                        scopeFactory, subMgr, rs, pSettings, resourceNameFormatter);
                });
        }
    }
}
