namespace NServiceBus.Transport.IBMMQ;

using System.Collections.Generic;
using IBM.WMQ;

/// <summary>
/// Configuration settings for IBM MQ transport
/// </summary>
public class IBMMQTransportOptions
{
    // Core Connection Settings

    /// <summary>
    /// Name of the Queue Manager to connect to.
    /// Can be null or empty to use the default local queue manager.
    /// </summary>
    public string? QueueManagerName { get; set; } = string.Empty;

    /// <summary>
    /// Hostname where the Queue Manager is running.
    /// Only used when ConnectionNameList is not specified.
    /// Default: "localhost"
    /// </summary>
    public string? Host { get; set; } = "localhost";

    /// <summary>
    /// Port number on which the Queue Manager is listening.
    /// Only used when ConnectionNameList is not specified.
    /// Default: 1414
    /// </summary>
    public int Port { get; set; } = 1414;

    /// <summary>
    /// Connection channel name.
    /// Default: "SYSTEM.DEF.SVRCONN"
    /// </summary>
    public string? Channel { get; set; } = "DEV.ADMIN.SVRCONN";

    /// <summary>
    /// List of connection names for high availability scenarios.
    /// Format: "hostname1(port1),hostname2(port2),..."
    /// Example: "mqhost1(1414),mqhost2(1414)"
    /// When set, this takes precedence over Host and Port properties.
    /// </summary>
    public List<string> Connections { get; } = [];

    //  Security Settings

    /// <summary>
    /// User ID for authentication with the Queue Manager.
    /// </summary>
    public string? User { get; set; }

    /// <summary>
    /// Password for authentication with the Queue Manager.
    /// </summary>
    public string? Password { get; set; }


    /// <summary>
    /// Application name to identify this connection in IBM MQ monitoring tools.
    /// This appears in queue manager connection lists (DISPLAY CONN) and helps with troubleshooting.
    /// If not specified, defaults to the assembly name.
    /// </summary>
    public string? ApplicationName { get; set; }

    //  SSL/TLS Settings

    /// <summary>
    /// SSL key repository location.
    /// Can be "*SYSTEM" (Windows certificate store), "*USER" (user certificate store),
    /// or a path to a key database file (without extension).
    /// Required for SSL/TLS connections.
    /// Example: "*SYSTEM" or "C:\mqm\ssl\key"
    /// </summary>
    public string? SslKeyRepository { get; set; }

    /// <summary>
    /// SSL cipher specification defining the encryption algorithm.
    /// Must match the SSLCIPH attribute on the SVRCONN channel.
    /// Example: "TLS_RSA_WITH_AES_128_CBC_SHA256" or "TLS_AES_256_GCM_SHA384"
    /// Required for SSL/TLS connections.
    /// </summary>
    public string? CipherSpec { get; set; }

    /// <summary>
    /// Distinguished name (DN) pattern of the server certificate for SSL peer name checking.
    /// Used to validate the queue manager's certificate.
    /// Example: "CN=MQSERVER01,O=MyCompany,C=US"
    /// Optional but recommended for secure SSL connections.
    /// </summary>
    public string? SslPeerName { get; set; }

    /// <summary>
    /// SSL key reset count.
    /// Specifies the number of bytes sent and received before renegotiating the secret key.
    /// 0 means disabled (use channel or queue manager setting).
    /// Typical value: 40000 (40KB)
    /// Default: 0 (disabled)
    /// </summary>
    public int KeyResetCount { get; set; } = 0;

    //  Message Processing Settings

    /// <summary>
    /// Time in milliseconds to wait for a message when polling a queue.
    /// Longer values reduce CPU usage but increase message processing latency.
    /// Shorter values improve responsiveness but increase CPU usage.
    /// Default: 5000ms (5 seconds)
    /// Valid range: 100-30000ms
    /// </summary>
    public TimeSpan MessageWaitInterval { get; set; } = TimeSpan.FromMilliseconds(5000);

    /// <summary>
    /// Maximum message size in bytes that can be received.
    /// Shared larger than this will cause an error.
    /// Should match or be less than the queue manager's MAXMSGL setting.
    /// Default: 4194304 bytes (4MB)
    /// Valid range: 1024-104857600 (1KB-100MB)
    /// </summary>
    public int MaxMessageLength { get; set; } = 4 * 1024 * 1024; // 4MB

    /// <summary>
    /// Coded Character Set Identifier (CCSID) for message text encoding.
    /// Common values:
    /// - 1208: UTF-8 (recommended, default)
    /// - 819: ISO 8859-1 (Latin-1)
    /// - 437: US English
    /// - 1252: Windows Latin-1
    /// Default: CODESET_UTF / 1208 (UTF-8)
    /// </summary>
    public int CharacterSet { get; set; } = MQC.CODESET_UTF;

    /// <summary>
    /// Controls how events are mapped to IBM MQ topics for pub/sub.
    /// Default: <see cref="TopicTopology.TopicPerEvent"/> (flat topology, one topic per concrete event type).
    /// </summary>
    public TopicTopology Topology { get; set; } = TopicTopology.TopicPerEvent();

    /// <summary>
    /// Controls how event types are mapped to IBM MQ topic names and topic strings.
    /// Subclass <see cref="IBMMQ.TopicNaming"/> to customize naming conventions for your environment.
    /// Default: <see cref="IBMMQ.TopicNaming"/> with prefix "DEV".
    /// </summary>
    public TopicNaming TopicNaming { get; set; } = new();

    /// <summary>
    /// The time to wait before triggering the circuit breaker when the endpoint
    /// cannot communicate with the broker. When the circuit breaker triggers,
    /// the critical error action is invoked.
    /// Default: 2 minutes.
    /// </summary>
    public TimeSpan TimeToWaitBeforeTriggeringCircuitBreaker
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value.Ticks, nameof(TimeToWaitBeforeTriggeringCircuitBreaker));
            field = value;
        }
    } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Sanitizer for queue resource names.
    /// Override to customize sanitization (e.g., replacing invalid characters, truncating long names).
    /// </summary>
    public SanitizeResourceName ResourceNameSanitizer { get; set; } = s => s;
}