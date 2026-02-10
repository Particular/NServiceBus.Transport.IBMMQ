namespace NServiceBus.TransportTests;

using System;
using System.Threading;
using System.Threading.Tasks;
using NServiceBus.Transport;
using NServiceBus.Transport.IbmMq;

public class ConfigureIbmMqTransportInfrastructure : IConfigureTransportInfrastructure
{
    static readonly string ConnectionDetails = Environment.GetEnvironmentVariable("IbmMq_ConnectionDetails") ?? "localhost;admin;passw0rd";

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

        var transport = new IbmMqTransport(s =>
        {
            if (port.HasValue)
            {
                s.Port = port.Value;
            }

            s.Host = host;
            s.Password = password;
            s.User = user;
            s.MessageWaitInterval = 1000;
            s.QueueNameFormatter = Format;

        });

        return transport;
    }

    static string Format(string name)
    {
        if (name.Length > 48)
        {
            var hash = name.GetHashCode().ToString("X8");
            name = name.Substring(0, 48 - 9) + "_" + hash; // 39 chars + "_" + 8 char hash = 48
        }
        return name;
    }

    public async Task<TransportInfrastructure> Configure(
        TransportDefinition transportDefinition,
        HostSettings hostSettings,
        QueueAddress inputQueueName,
        string errorQueueName,
        CancellationToken cancellationToken = default
    )
    {
        var receiveSettings =
            new ReceiveSettings(
                "mainReceiver",
                inputQueueName,
                true,
                false,
                errorQueueName
            );

        var transportInfrastructure = await transportDefinition.Initialize(
            hostSettings,
            [receiveSettings],
            [],
            cancellationToken
        ).ConfigureAwait(false);

        queuesToCleanUp = [transportInfrastructure.ToTransportAddress(inputQueueName), errorQueueName];

        return transportInfrastructure;
    }

    public Task Cleanup(CancellationToken cancellationToken = default)
    {
        if (queuesToCleanUp == null)
        {
            // TODO: Do we need to clean up queues?
        }
        return Task.CompletedTask;
    }

    string[] queuesToCleanUp;
}