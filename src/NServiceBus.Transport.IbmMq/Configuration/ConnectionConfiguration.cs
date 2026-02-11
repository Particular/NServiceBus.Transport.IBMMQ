namespace NServiceBus.Transport.IbmMq;

using IBM.WMQ;
using System.Collections;
using System.Reflection;

/// <summary>
/// Builds IBM MQ connection properties hashtable from transport settings
/// </summary>
class ConnectionConfiguration
{
    public ConnectionConfiguration(IbmMqTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        QueueManagerName = options.QueueManagerName ?? string.Empty;
        ConnectionProperties = BuildConnectionProperties(options);
        MessageWaitInterval = options.MessageWaitInterval;
    }

    public TimeSpan MessageWaitInterval { get; }

    public string QueueManagerName { get; }

    public Hashtable ConnectionProperties { get; }

    static Hashtable BuildConnectionProperties(IbmMqTransportOptions options)
    {
        var properties = new Hashtable
        {
            // Always use managed transport mode (pure .NET implementation)
            [MQC.TRANSPORT_PROPERTY] = MQC.TRANSPORT_MQSERIES_MANAGED
        };

        properties.Add(MQC.CHANNEL_PROPERTY, options.Channel);

        properties.Add(MQC.CONNECT_OPTIONS_PROPERTY, MQC.MQCNO_RECONNECT_DISABLED);

        var appName = options.ApplicationName ?? Assembly.GetExecutingAssembly().GetName().Name ?? "NServiceBus.IbmMq";
        properties.Add(MQC.APPNAME_PROPERTY, appName);

        AddSslProperties(properties, options);

        if (!string.IsNullOrWhiteSpace(options.User))
        {
            properties.Add(MQC.USER_ID_PROPERTY, options.User);
        }

        if (!string.IsNullOrWhiteSpace(options.Password))
        {
            properties.Add(MQC.PASSWORD_PROPERTY, options.Password);
        }

        return properties;
    }

    static void AddSslProperties(Hashtable properties, IbmMqTransportOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.SslKeyRepository))
        {
            properties.Add(MQC.SSL_CERT_STORE_PROPERTY, options.SslKeyRepository);
        }

        if (!string.IsNullOrWhiteSpace(options.CipherSpec))
        {
            properties.Add(MQC.SSL_CIPHER_SPEC_PROPERTY, options.CipherSpec);
        }

        if (!string.IsNullOrWhiteSpace(options.SslPeerName))
        {
            properties.Add(MQC.SSL_PEER_NAME_PROPERTY, options.SslPeerName);
        }

        if (options.KeyResetCount > 0)
        {
            properties.Add(MQC.SSL_RESET_COUNT_PROPERTY, options.KeyResetCount);
        }
    }
}