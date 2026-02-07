namespace NServiceBus.Transport.IbmMq;

public class IbmMqTransport : TransportDefinition
{
    IbmMqTransportOptions Options { get; }

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

    public override Task<TransportInfrastructure> Initialize(HostSettings hostSettings, ReceiveSettings[] receivers, string[] sendingAddresses, CancellationToken cancellationToken = default)
    {
        var connectionConfiguration = new ConnectionConfiguration(Options);
        var infrastructure = new IbmMqTransportInfrastructure(connectionConfiguration, receivers);
        return Task.FromResult<TransportInfrastructure>(infrastructure);
    }
}