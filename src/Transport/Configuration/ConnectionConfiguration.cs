namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
using System.Collections;
using System.Reflection;

/// <summary>
/// Builds IBM MQ connection properties hashtable from transport settings
/// </summary>
class ConnectionConfiguration
{
    public ConnectionConfiguration(IBMMQTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);

        QueueManagerName = transport.QueueManagerName ?? string.Empty;
        ConnectionProperties = BuildConnectionProperties(transport, out var applicationName);
        ApplicationName = applicationName;
        MessageWaitInterval = transport.MessageWaitInterval;
    }

    public string ApplicationName { get; }

    public TimeSpan MessageWaitInterval { get; }

    public string QueueManagerName { get; }

    public Hashtable ConnectionProperties { get; }

    static Hashtable BuildConnectionProperties(IBMMQTransport transport, out string applicationName)
    {
        var properties = new Hashtable
        {
            // Always use managed transport mode (pure .NET implementation)
            [MQC.TRANSPORT_PROPERTY] = MQC.TRANSPORT_MQSERIES_MANAGED
        };

        if (transport.Connections.Count > 0)
        {
            properties.Add(MQC.CONNECTION_NAME_PROPERTY, string.Join(",", transport.Connections));
        }
        else
        {
            properties.Add(MQC.HOST_NAME_PROPERTY, transport.Host);
            properties.Add(MQC.PORT_PROPERTY, transport.Port);
        }

        properties.Add(MQC.CHANNEL_PROPERTY, transport.Channel);

        properties.Add(MQC.CONNECT_OPTIONS_PROPERTY, MQC.MQCNO_RECONNECT_DISABLED);

        applicationName = transport.ApplicationName ?? Assembly.GetExecutingAssembly().GetName().Name ?? "NServiceBus.IBMMQ";
        properties.Add(MQC.APPNAME_PROPERTY, applicationName);

        AddSslProperties(properties, transport);

        if (!string.IsNullOrWhiteSpace(transport.User))
        {
            properties.Add(MQC.USE_MQCSP_AUTHENTICATION_PROPERTY, true);
            properties.Add(MQC.USER_ID_PROPERTY, transport.User);
        }

        if (!string.IsNullOrWhiteSpace(transport.Password))
        {
            properties.Add(MQC.PASSWORD_PROPERTY, transport.Password);
        }

        return properties;
    }

    static void AddSslProperties(Hashtable properties, IBMMQTransport transport)
    {
        if (!string.IsNullOrWhiteSpace(transport.SslKeyRepository))
        {
            properties.Add(MQC.SSL_CERT_STORE_PROPERTY, transport.SslKeyRepository);
        }

        if (!string.IsNullOrWhiteSpace(transport.CipherSpec))
        {
            properties.Add(MQC.SSL_CIPHER_SPEC_PROPERTY, transport.CipherSpec);
        }

        if (!string.IsNullOrWhiteSpace(transport.SslPeerName))
        {
            properties.Add(MQC.SSL_PEER_NAME_PROPERTY, transport.SslPeerName);
        }

        if (transport.KeyResetCount > 0)
        {
            properties.Add(MQC.SSL_RESET_COUNT_PROPERTY, transport.KeyResetCount);
        }
    }
}
