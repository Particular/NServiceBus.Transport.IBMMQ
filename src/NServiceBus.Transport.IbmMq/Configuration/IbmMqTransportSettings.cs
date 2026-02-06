using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using IBM.WMQ;

namespace NServiceBus.Transport.IbmMq.Configuration;

/// <summary>
/// Configuration settings for IBM MQ transport
/// </summary>
public class IbmMqTransportSettings
{
    // Core Connection Settings

    /// <summary>
    /// Name of the Queue Manager to connect to.
    /// Can be null or empty to use the default local queue manager.
    /// </summary>
    public string QueueManagerName { get; set; } = "";

    /// <summary>
    /// Hostname where the Queue Manager is running.
    /// Only used when ConnectionNameList is not specified.
    /// Default: "localhost"
    /// </summary>
    public string Host { get; set; } = "localhost";

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
    public string Channel { get; set; } = "DEV.ADMIN.SVRCONN";

    /// <summary>
    /// List of connection names for high availability scenarios.
    /// Format: "hostname1(port1),hostname2(port2),..."
    /// Example: "mqhost1(1414),mqhost2(1414)"
    /// When set, this takes precedence over Host and Port properties.
    /// </summary>
    public List<string>? Connections { get; set; }

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
    public int MessageWaitInterval { get; set; } = 5000;

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
    /// Default: 1208 (UTF-8)
    /// </summary>
    public int CharacterSet { get; set; } = 1208; // UTF-8



    /// <summary>
    /// Validates the settings and throws ArgumentException if any are invalid.
    /// </summary>
    internal void Validate()
    {
        // Queue Manager validation - can be null for local default QM
        // No validation needed for QueueManagerName as it can be null

        // Connection validation
        ValidateConnection();

        // SSL validation
        ValidateSslConfiguration();

        // Message processing validation
        ValidateMessageProcessing();

        // Heartbeat validation

    }

    private void ValidateConnection()
    {
        // Either ConnectionNameList OR Host+Port must be valid
        if (Connections is not null && Connections.Count() > 0)
        {
            // Validate ConnectionNameList format
            if (!IsValidConnectionNameList(Connections))
            {
                throw new ArgumentException(
                    "Connections format is invalid. Expected format: 'host1(port1),host2(port2)'",
                    nameof(Connections));
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Host))
            {
                throw new ArgumentException(
                    "Host is required when ConnectionNameList is not specified",
                    nameof(Host));
            }

            if (Port <= 0 || Port > 65535)
            {
                throw new ArgumentException(
                    "Port must be between 1 and 65535",
                    nameof(Port));
            }
        }

        if (string.IsNullOrWhiteSpace(Channel))
        {
            throw new ArgumentException("Channel is required", nameof(Channel));
        }
    }

    private void ValidateSslConfiguration()
    {
        bool hasKeyRepo = !string.IsNullOrWhiteSpace(SslKeyRepository);
        bool hasCipherSpec = !string.IsNullOrWhiteSpace(CipherSpec);

        // SSL settings must be specified together
        if (hasKeyRepo && !hasCipherSpec)
        {
            throw new ArgumentException(
                "CipherSpec is required when SslKeyRepository is specified",
                nameof(CipherSpec));
        }

        if (hasCipherSpec && !hasKeyRepo)
        {
            throw new ArgumentException(
                "SslKeyRepository is required when CipherSpec is specified",
                nameof(SslKeyRepository));
        }

        if (KeyResetCount < 0)
        {
            throw new ArgumentException(
                "KeyResetCount must be 0 or greater",
                nameof(KeyResetCount));
        }
    }

    private void ValidateMessageProcessing()
    {
        if (MessageWaitInterval < 100 || MessageWaitInterval > 30000)
        {
            throw new ArgumentException(
                "MessageWaitInterval must be between 100 and 30000 milliseconds",
                nameof(MessageWaitInterval));
        }

        if (MaxMessageLength < 1024 || MaxMessageLength > 104857600)
        {
            throw new ArgumentException(
                "MaxMessageLength must be between 1024 (1KB) and 104857600 (100MB) bytes",
                nameof(MaxMessageLength));
        }

        if (CharacterSet <= 0)
        {
            throw new ArgumentException(
                "CharacterSet must be a positive CCSID value",
                nameof(CharacterSet));
        }
    }

    private static bool IsValidConnectionNameList(List<string> connectionNameList)
    {
        // Basic validation: should contain at least one entry in format "host(port)"

        if (!connectionNameList.Any())
            return false;

        foreach (var entry in connectionNameList)
        {
            var trimmed = entry.Trim();

            // Must contain opening and closing parentheses
            if (!trimmed.Contains('(') || !trimmed.Contains(')'))
                return false;

            // Extract port to validate it's a number
            var startParen = trimmed.IndexOf('(');
            var endParen = trimmed.IndexOf(')');

            if (startParen >= endParen || startParen == 0)
                return false;

            var portStr = trimmed.Substring(startParen + 1, endParen - startParen - 1);

            if (!int.TryParse(portStr, out var port) || port <= 0 || port > 65535)
                return false;
        }

        return true;
    }
}

/// <summary>
/// Client reconnection behavior options
/// </summary>
public enum ReconnectOption
{
    /// <summary>
    /// No automatic reconnection (MQCNO_RECONNECT_DISABLED).
    /// Application must handle connection failures manually.
    /// </summary>
    Disabled = 0,

    /// <summary>
    /// Reconnect to any available queue manager (MQCNO_RECONNECT).
    /// Use for client applications connecting to a queue manager group.
    /// </summary>
    Reconnect = 1,

    /// <summary>
    /// Reconnect to the same queue manager only (MQCNO_RECONNECT_Q_MGR).
    /// Recommended for most scenarios (default).
    /// </summary>
    ReconnectQueueManager = 2,

    /// <summary>
    /// Use reconnect options specified in mqclient.ini file (MQCNO_RECONNECT_AS_DEF).
    /// Allows centralized configuration management.
    /// </summary>
    ReconnectAsDefinedInConfig = 3
}

