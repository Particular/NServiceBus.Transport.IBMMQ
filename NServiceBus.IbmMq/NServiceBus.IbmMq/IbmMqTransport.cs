using IBM.WMQ;
using NServiceBus.Transport;

namespace NServiceBus.IbmMq;

public class IbmMqTransport : TransportDefinition
{
    public string Host { get; set; }
    public int Port { get; set; } = 1414;
    public string User { get; set; }
    public string Password { get; set; }
    public string Channel { get; set; } = "DEV.ADMIN.SVRCONN"; // a level of authorization. We do not want to use Admin in a production environment but need to figure out queue permisions

    public IbmMqTransport(string host, string user, string password, int? port, string? channel) : base(TransportTransactionMode.ReceiveOnly, true, true, true)
    {
        Host = host;
        User = user;
        Password = password;
        if (port != null) Port = port.Value;
        if (channel != null) Channel = channel;
    }

    public override IReadOnlyCollection<TransportTransactionMode> GetSupportedTransactionModes()
    {
        return [
            TransportTransactionMode.None,
            TransportTransactionMode.ReceiveOnly
        ];
    }

    public override Task<TransportInfrastructure> Initialize(HostSettings hostSettings, ReceiveSettings[] receivers, string[] sendingAddresses, CancellationToken cancellationToken = default)
    {
        MQEnvironment.Hostname = Host;
        MQEnvironment.Channel = Channel;
        MQEnvironment.Port = Port;
        MQEnvironment.UserId = User;
        MQEnvironment.Password = Password;

        return Task.FromResult<TransportInfrastructure>(new IbmMqTransportInfrastructure(receivers));
    }
}
