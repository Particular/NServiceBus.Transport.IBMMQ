namespace NServiceBus.TransportTests;

using System;
using System.Threading;
using System.Threading.Tasks;
using NServiceBus.Transport;
using NServiceBus.Transport.IbmMq;

public class ConfigureIbmMqTransportInfrastructure : IConfigureTransportInfrastructure
{
    public TransportDefinition CreateTransportDefinition()
    {
        var transport = new IbmMqTransport(s =>
        {
            TestConnectionDetails.Apply(s);
            s.MessageWaitInterval = TimeSpan.FromMilliseconds(100);
            s.ResourceNameSanitizer = Sanitize;
        });

        return transport;
    }

    static string Sanitize(string name)
    {
        name = name
            .Replace('-', '.');

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