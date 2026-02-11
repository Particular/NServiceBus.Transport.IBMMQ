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
        ReceiveSettings[] receiverSettings
    )
    {
        this.log = log;
        ArgumentNullException.ThrowIfNull(connectionConfiguration);
        ArgumentNullException.ThrowIfNull(receiverSettings);

        var services = new ServiceCollection();
        ConfigureServices(services, options, connectionConfiguration, receiverSettings);
        serviceProvider = services.BuildServiceProvider();

        Dispatcher = serviceProvider.GetRequiredService<IbmMqMessageDispatcher>();
        Receivers = serviceProvider.GetServices<IMessageReceiver>()
            .ToDictionary(r => r.Id);
    }

    public override string ToTransportAddress(QueueAddress address) => address.BaseAddress;

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
        ReceiveSettings[] receiverSettings)
    {
        var queueManagerName = connectionConfiguration.QueueManagerName;
        var connectionProperties = connectionConfiguration.ConnectionProperties;
        var messageWaitInterval = connectionConfiguration.MessageWaitInterval;
        FormatQueueName queueNameFormatter = options.QueueNameFormatter;

        services
            .AddSingleton<CreateQueueManager>(() => new MQQueueManager(queueManagerName, connectionProperties))
            .AddSingleton(new MessagePumpSettings(messageWaitInterval))
            .AddScoped(sp => new MessagePumpWorker(
                LogManager.GetLogger<MessagePumpWorker>(),
                sp.GetRequiredService<MessagePumpSettings>(),
                sp.GetRequiredService<CreateQueueManager>()
            ))
            .AddSingleton<CreateQueueManagerFacade>(qm =>
                new MqQueueManagerFacade(qm, queueNameFormatter))
            .AddSingleton(sp =>
            {
                var sendConnection = new MQQueueManager(queueManagerName, connectionProperties);
                var createFacade = sp.GetRequiredService<CreateQueueManagerFacade>();
                return new IbmMqMessageDispatcher(
                    LogManager.GetLogger<IbmMqMessageDispatcher>(),
                    sendConnection,
                    createFacade(sendConnection));
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
                    return new IbmMqMessageReceiver(
                        LogManager.GetLogger<IbmMqMessageReceiver>(),
                        scopeFactory, subMgr, rs);
                });
        }
    }
}
