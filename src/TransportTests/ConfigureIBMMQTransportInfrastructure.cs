namespace NServiceBus.TransportTests;

using NServiceBus.Transport;
using NServiceBus.Transport.IBMMQ;

public class ConfigureIBMMQTransportInfrastructure : IConfigureTransportInfrastructure
{
    public TransportDefinition CreateTransportDefinition()
    {
        var transport = new IBMMQTransport
        {
            MessageWaitInterval = TimeSpan.FromMilliseconds(100),
            TopicNaming = TestConnectionDetails.CreateTopicNaming(),
            ResourceNameSanitizer = Sanitize
        };
        TestConnectionDetails.Apply(transport);

        return transport;
    }

    static string Sanitize(string name)
    {
        name = name
            .Replace('-', '.');

        if (name.Length > 48)
        {
            var hash = name.GetHashCode().ToString("X8");
            name = name[..(48 - 9)] + "_" + hash; // 39 chars + "_" + 8 char hash = 48
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
