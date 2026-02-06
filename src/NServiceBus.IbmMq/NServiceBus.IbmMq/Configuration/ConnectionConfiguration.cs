using IBM.WMQ;
using System.Collections;
using System.Reflection;

namespace NServiceBus.Transport.IbmMq.Configuration;

/// <summary>
/// Builds IBM MQ connection properties hashtable from transport settings
/// </summary>
internal class ConnectionConfiguration
{
    public ConnectionConfiguration(IbmMqTransportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        QueueManagerName = settings.QueueManagerName ?? string.Empty;
        ConnectionProperties = BuildConnectionProperties(settings);
    }

    public ConnectionConfiguration(string queueManagerName, Hashtable connectionProperties)
    {
        QueueManagerName = queueManagerName ?? string.Empty;
        ConnectionProperties = connectionProperties ?? throw new ArgumentNullException(nameof(connectionProperties));
    }

    public string QueueManagerName { get; }

    public Hashtable ConnectionProperties { get; }

    static Hashtable BuildConnectionProperties(IbmMqTransportSettings settings)
    {
        var properties = new Hashtable
        {
            // Always use managed transport mode (pure .NET implementation)
            [MQC.TRANSPORT_PROPERTY] = MQC.TRANSPORT_MQSERIES_MANAGED
        };

        properties.Add(MQC.CHANNEL_PROPERTY, settings.Channel);

        properties.Add(MQC.CONNECT_OPTIONS_PROPERTY, MQC.MQCNO_RECONNECT_DISABLED);

        var appName = settings.ApplicationName ?? Assembly.GetExecutingAssembly().GetName().Name ?? "NServiceBus.IbmMq";
        properties.Add(MQC.APPNAME_PROPERTY, appName);

        AddSslProperties(properties, settings);

        if (!string.IsNullOrWhiteSpace(settings.User))
        {
            properties.Add(MQC.USER_ID_PROPERTY, settings.User);
        }

        if (!string.IsNullOrWhiteSpace(settings.Password))
        {
            properties.Add(MQC.PASSWORD_PROPERTY, settings.Password);
        }

        return properties;
    }

    static void AddSslProperties(Hashtable properties, IbmMqTransportSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.SslKeyRepository))
        {
            properties.Add(MQC.SSL_CERT_STORE_PROPERTY, settings.SslKeyRepository);
        }

        if (!string.IsNullOrWhiteSpace(settings.CipherSpec))
        {
            properties.Add(MQC.SSL_CIPHER_SPEC_PROPERTY, settings.CipherSpec);
        }

        if (!string.IsNullOrWhiteSpace(settings.SslPeerName))
        {
            properties.Add(MQC.SSL_PEER_NAME_PROPERTY, settings.SslPeerName);
        }

        if (settings.KeyResetCount > 0)
        {
            properties.Add(MQC.SSL_RESET_COUNT_PROPERTY, settings.KeyResetCount);
        }
    }
}