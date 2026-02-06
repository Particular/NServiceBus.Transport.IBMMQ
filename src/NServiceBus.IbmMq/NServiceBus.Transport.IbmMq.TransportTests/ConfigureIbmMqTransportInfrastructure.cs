using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using IBM.WMQ;
using Microsoft.ApplicationInsights;
using NServiceBus.Transport;
using NServiceBus.Transport.IbmMq;

namespace NServiceBus.TransportTests;

public class ConfigureIbmMqTransportInfrastructure : IConfigureTransportInfrastructure
{
    private static readonly string ConnectionDetails =
        Environment.GetEnvironmentVariable("IbmMq_ConnectionDetails") ?? "localhost;admin;passw0rd";

    public TransportDefinition CreateTransportDefinition()
    {
        var parts = ConnectionDetails.Split(';');

        var host = parts.Length > 0 ? parts[0] : string.Empty;
        var user = parts.Length > 1 ? parts[1] : string.Empty;
        var password = parts.Length > 2 ? parts[2] : string.Empty;
        var portString = parts.Length > 3 ? parts[3] : string.Empty;
        int? port = string.IsNullOrWhiteSpace(portString) ? null : int.Parse(portString);

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("Host is required in IbmMq_ConnectionDetails");
        }

        if (string.IsNullOrWhiteSpace(user))
        {
            throw new InvalidOperationException("User is required in IbmMq_ConnectionDetails");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Password is required in IbmMq_ConnectionDetails");
        }

        var transport = new IbmMqTransport(host, user, password, port, null);
        return transport;
    }

    public async Task<TransportInfrastructure> Configure(TransportDefinition transportDefinition,
        HostSettings hostSettings, QueueAddress inputQueueName, string errorQueueName,
        CancellationToken cancellationToken = default)
    {
        var receiveSettings =
            new ReceiveSettings(
                "mainReceiver",
                inputQueueName,
                true,
                false,
                errorQueueName);

        var transportInfrastructure = await transportDefinition.Initialize(
            hostSettings, [receiveSettings], [], cancellationToken);

        queuesToCleanUp = [transportInfrastructure.ToTransportAddress(inputQueueName), errorQueueName];

        return transportInfrastructure;
    }

    //TODO: Do we need to clean up queues?
    public async Task Cleanup(CancellationToken cancellationToken = default)
    {
        if (queuesToCleanUp == null)
        {
            return;
        }
        
        // TODO: Purge queues
    }

    string[] queuesToCleanUp;
}