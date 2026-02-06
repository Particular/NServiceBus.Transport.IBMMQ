namespace NServiceBus.Transport.IbmMq;

using NServiceBus.Transport.IbmMq.Configuration;

public class IbmMqTransport : TransportDefinition
{
    internal IbmMqTransportSettings Settings { get; }

    /// <summary>
    /// Creates a new instance of IBM MQ transport with configuration
    /// </summary>
    /// <param name="settingsToConfigure">Lambda to configure transport settings</param>
    public IbmMqTransport(Action<IbmMqTransportSettings> settingsToConfigure)
        : base(TransportTransactionMode.ReceiveOnly, true, true, true)
    {
        ArgumentNullException.ThrowIfNull(settingsToConfigure);

        Settings = new IbmMqTransportSettings();
        settingsToConfigure(Settings);
        Settings.Validate();
    }

    /// <summary>
    /// Internal constructor for creating transport with pre-configured settings
    /// </summary>
    internal IbmMqTransport(IbmMqTransportSettings settings)
        : base(TransportTransactionMode.ReceiveOnly, true, true, true)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Settings.Validate();
    }


    public override IReadOnlyCollection<TransportTransactionMode> GetSupportedTransactionModes()
    {
        return
        [
            TransportTransactionMode.None,
            TransportTransactionMode.ReceiveOnly
        ];
    }


    public override Task<TransportInfrastructure> Initialize(HostSettings hostSettings, ReceiveSettings[] receivers, string[] sendingAddresses, CancellationToken cancellationToken = default)
    {
        var connectionConfiguration = new ConnectionConfiguration(Settings);

        return Task.FromResult<TransportInfrastructure>(
            new IbmMqTransportInfrastructure(connectionConfiguration, receivers));
    }
}