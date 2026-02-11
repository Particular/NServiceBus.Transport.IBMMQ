namespace NServiceBus.Transport.IbmMq;

using IBM.WMQ;
using Logging;

public class IbmMqTransport : TransportDefinition
{
    readonly ILog log = LogManager.GetLogger<IbmMqTransport>();

    internal IbmMqTransportOptions Options { get; }

    /// <summary>
    /// Creates a new instance of IBM MQ transport with configuration
    /// </summary>
    /// <param name="settingsToConfigure">Lambda to configure transport settings</param>
    public IbmMqTransport(Action<IbmMqTransportOptions> configure)
        : base(TransportTransactionMode.ReceiveOnly, true, true, true)
    {
        ArgumentNullException.ThrowIfNull(configure);

        Options = new IbmMqTransportOptions();
        configure(Options);
        new IbmMqTransportOptionsValidate().Validate(Options);
    }

    /// <summary>
    /// Internal constructor for creating transport with pre-configured settings
    /// </summary>
    internal IbmMqTransport(IbmMqTransportOptions options)
        : base(TransportTransactionMode.ReceiveOnly, true, true, true)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        new IbmMqTransportOptionsValidate().Validate(Options);
    }

    public override IReadOnlyCollection<TransportTransactionMode> GetSupportedTransactionModes() =>
    [
        TransportTransactionMode.None,
        TransportTransactionMode.ReceiveOnly
    ];

    public override Task<TransportInfrastructure> Initialize(
        HostSettings hostSettings,
        ReceiveSettings[] receivers,
        string[] sendingAddresses,
        CancellationToken cancellationToken = default
    )
    {
        var connectionConfiguration = new ConnectionConfiguration(Options);

        var queueManager = new MQQueueManager(Options.QueueManagerName, connectionConfiguration.ConnectionProperties);
        var manager = new IbmMqHelper(log, queueManager, Options.QueueNameFormatter);

        if (hostSettings.SetupInfrastructure)
        {
            foreach (var receiver in receivers)
            {
                log.DebugFormat("Creating queue {0}", receiver.ReceiveAddress.BaseAddress);
                manager.CreateQueue(receiver.ReceiveAddress.BaseAddress);
            }
        }

        foreach (var receiver in receivers)
        {
            if (receiver.PurgeOnStartup)
            {
                log.DebugFormat("Purging queue {0}", receiver.ReceiveAddress.BaseAddress);
                manager.PurgeQueue(receiver.ReceiveAddress.BaseAddress);
            }
        }

        var infrastructure = new IbmMqTransportInfrastructure(Options, connectionConfiguration, receivers);
        return Task.FromResult<TransportInfrastructure>(infrastructure);
    }
}