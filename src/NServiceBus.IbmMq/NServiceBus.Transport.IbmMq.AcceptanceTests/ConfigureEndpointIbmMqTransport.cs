using System;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.AcceptanceTesting.Support;
using NServiceBus.Transport.IbmMq;

// ReSharper disable once CheckNamespace
public class ConfigureEndpointIbmMqTransport : IConfigureEndpointTestExecution
{
    private static readonly string ConnectionDetails =
        Environment.GetEnvironmentVariable("IbmMq_ConnectionDetails") ?? "localhost;admin;passw0rd";
    
    public Task Configure(string endpointName, EndpointConfiguration configuration, RunSettings settings,
        PublisherMetadata publisherMetadata)
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

        configuration.UseTransport(transport);

        return Task.CompletedTask;
    }

    //TODO: Do we need to clean up queues?
    public Task Cleanup() => Task.CompletedTask;
}