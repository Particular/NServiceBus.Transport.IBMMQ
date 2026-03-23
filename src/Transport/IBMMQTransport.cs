namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
using IBM.WMQ.PCF;
using Logging;

/// <summary>
/// IBM MQ transport for NServiceBus.
/// </summary>
public sealed class IBMMQTransport : TransportDefinition
{
    readonly ILog log = LogManager.GetLogger<IBMMQTransport>();

    // Core Connection Settings

    /// <summary>
    /// Name of the Queue Manager to connect to.
    /// Can be null or empty to use the default local queue manager.
    /// </summary>
    public string? QueueManagerName { get; set; } = string.Empty;

    /// <summary>
    /// Hostname where the Queue Manager is running.
    /// Only used when Connections is not specified.
    /// Default: "localhost"
    /// </summary>
    public string? Host { get; set; } = "localhost";

    /// <summary>
    /// Port number on which the Queue Manager is listening.
    /// Only used when Connections is not specified.
    /// Default: 1414
    /// </summary>
    public int Port
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 65535);
            field = value;
        }
    } = 1414;

    /// <summary>
    /// Connection channel name.
    /// Default: "DEV.ADMIN.SVRCONN"
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
    public int KeyResetCount
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    //  Message Processing Settings

    /// <summary>
    /// Time in milliseconds to wait for a message when polling a queue.
    /// Longer values reduce CPU usage but increase message processing latency.
    /// Shorter values improve responsiveness but increase CPU usage.
    /// Default: 5000ms (5 seconds)
    /// Valid range: 100-30000ms
    /// </summary>
    public TimeSpan MessageWaitInterval
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.FromMilliseconds(100));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, TimeSpan.FromMilliseconds(30000));
            field = value;
        }
    } = TimeSpan.FromMilliseconds(5000);

    /// <summary>
    /// Coded Character Set Identifier (CCSID) for the message body.
    /// This value is set on the MQMessage's CharacterSet property (MQMD CodedCharSetId field)
    /// and describes the encoding of the payload.
    /// Common values:
    /// - 1208: UTF-8 (recommended, default) — correct for JSON and XML serializers
    /// - 819: ISO 8859-1 (Latin-1)
    /// - 437: US English
    /// - 1252: Windows Latin-1
    /// Default: CODESET_UTF / 1208 (UTF-8)
    /// </summary>
    public int CharacterSet
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = MQC.CODESET_UTF;

    /// <summary>
    /// Controls how events are mapped to IBM MQ topics for pub/sub.
    /// Default: <see cref="TopicTopology.TopicPerEvent"/> (flat topology, one topic per concrete event type).
    /// </summary>
    public TopicTopology Topology
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = TopicTopology.TopicPerEvent();

    /// <summary>
    /// Controls how event types are mapped to IBM MQ topic names and topic strings.
    /// Subclass <see cref="IBMMQ.TopicNaming"/> to customize naming conventions for your environment.
    /// Default: <see cref="IBMMQ.TopicNaming"/> with prefix "DEV".
    /// </summary>
    public TopicNaming TopicNaming
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = new();

    /// <summary>
    /// Sanitizer for queue resource names.
    /// Override to customize sanitization (e.g., replacing invalid characters, truncating long names).
    /// </summary>
    public SanitizeResourceName ResourceNameSanitizer
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = s => s;

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
    /// Creates a new instance of IBM MQ transport.
    /// </summary>
    public IBMMQTransport()
        : base(TransportTransactionMode.ReceiveOnly, supportsDelayedDelivery: false, supportsPublishSubscribe: true, supportsTTBR: true)
    {
    }

    /// <inheritdoc />
    public override IReadOnlyCollection<TransportTransactionMode> GetSupportedTransactionModes() =>
    [
        TransportTransactionMode.None,
        TransportTransactionMode.ReceiveOnly,
        TransportTransactionMode.SendsAtomicWithReceive
    ];

    /// <inheritdoc />
    public override Task<TransportInfrastructure> Initialize(
        HostSettings hostSettings,
        ReceiveSettings[] receivers,
        string[] sendingAddresses,
        CancellationToken cancellationToken = default
    )
    {
        IBMMQTransportValidator.Validate(this);

        var connectionConfiguration = new ConnectionConfiguration(this);

        using var setupConnection = new MQQueueManager(QueueManagerName, connectionConfiguration.ConnectionProperties);

        if (hostSettings.SetupInfrastructure)
        {
            foreach (var receiver in receivers)
            {
                var queueName = SanitizeQueueName(IBMMQMessageReceiver.ToTransportAddress(receiver.ReceiveAddress));
                log.DebugFormat("Creating queue {0}", queueName);
                CreateQueue(setupConnection, queueName);

                if (receiver.ErrorQueue != null)
                {
                    var errorQueueName = SanitizeQueueName(receiver.ErrorQueue);
                    log.DebugFormat("Creating error queue {0}", errorQueueName);
                    CreateQueue(setupConnection, errorQueueName);
                }
            }

            foreach (var sendingAddress in sendingAddresses)
            {
                var queueName = SanitizeQueueName(sendingAddress);
                log.DebugFormat("Creating send queue {0}", queueName);
                CreateQueue(setupConnection, queueName);
            }
        }

        foreach (var receiver in receivers)
        {
            if (receiver.PurgeOnStartup)
            {
                var queueName = SanitizeQueueName(IBMMQMessageReceiver.ToTransportAddress(receiver.ReceiveAddress));
                log.DebugFormat("Purging queue {0}", queueName);
                var count = PurgeQueue(setupConnection, queueName);
                log.InfoFormat("Purged {0} messages from queue '{1}'", count, queueName);
            }
        }

        WriteStartupDiagnostics(hostSettings.StartupDiagnostic, connectionConfiguration, receivers, TransportTransactionMode);
        WriteBrokerDiagnostics(log, hostSettings.StartupDiagnostic, setupConnection);

        setupConnection.Disconnect();

        var infrastructure = new IBMMQTransportInfrastructure(log, this, connectionConfiguration, receivers, TransportTransactionMode, hostSettings.CriticalErrorAction);
        return Task.FromResult<TransportInfrastructure>(infrastructure);
    }

    static void CreateQueue(MQQueueManager queueManager, string name)
    {
        var agent = new PCFMessageAgent(queueManager);
        try
        {
            var request = new PCFMessage(MQC.MQCMD_CREATE_Q);
            request.AddParameter(MQC.MQCA_Q_NAME, name);
            request.AddParameter(MQC.MQIA_Q_TYPE, MQC.MQQT_LOCAL);
            request.AddParameter(MQC.MQIA_MAX_Q_DEPTH, 5000);
            request.AddParameter(MQC.MQIA_DEF_PERSISTENCE, MQC.MQPER_PERSISTENT);

            agent.Send(request);
        }
        catch (PCFException e) when (e.ReasonCode == MQC.MQRCCF_OBJECT_ALREADY_EXISTS)
        {
            // Queue already exists, nothing to do
        }
        catch (PCFException e) when (e.ReasonCode == MQC.MQRCCF_CFST_STRING_LENGTH_ERR)
        {
            throw new Exception(
                $"IBM MQ rejected the queue name '{name}' because it exceeds the maximum allowed length (48 characters). " +
                $"Use the {nameof(ResourceNameSanitizer)} option to truncate or shorten queue names before they are sent to IBM MQ.", e);
        }
        catch (PCFException e) when (e.ReasonCode == MQC.MQRCCF_OBJECT_NAME_ERROR)
        {
            throw new Exception(
                $"IBM MQ rejected the queue name '{name}' because it contains invalid characters or is otherwise malformed. " +
                $"IBM MQ queue names must be 1-48 characters using only A-Z, 0-9, '.', '_', '/', and '%'. " +
                $"Use the {nameof(ResourceNameSanitizer)} option to transform queue names into a valid format before they are sent to IBM MQ.", e);
        }
        finally
        {
            agent.Disconnect();
        }
    }

    static int PurgeQueue(MQQueueManager queueManager, string name)
    {
        using var queue = queueManager.AccessQueue(name, MQC.MQOO_INPUT_EXCLUSIVE | MQC.MQOO_INQUIRE);
        var gmo = new MQGetMessageOptions
        {
            Options = MQC.MQGMO_NO_WAIT | MQC.MQGMO_ACCEPT_TRUNCATED_MSG
        };

        int count = 0;
        while (true)
        {
            try
            {
                var message = new MQMessage();
                queue.Get(message, gmo);
                message.ClearMessage();
                ++count;
            }
            catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
            {
                break;
            }
        }

        queue.Close();

        return count;
    }

    void WriteStartupDiagnostics(
        StartupDiagnosticEntries diagnostics,
        ConnectionConfiguration connectionConfiguration,
        ReceiveSettings[] receivers,
        TransportTransactionMode transactionMode)
    {

        diagnostics.Add("IBM MQ transport", new
        {
            TransactionMode = transactionMode.ToString(),
            Host,
            Port,
            Channel,
            Connections = Connections.Count > 0 ? string.Join(",", Connections) : null,
            QueueManagerName,
            connectionConfiguration.ApplicationName,
            Topology = Topology.GetType().ToString(),
            MessageWaitInterval = MessageWaitInterval.ToString(),
            CharacterSet,
            SslEnabled = !string.IsNullOrWhiteSpace(CipherSpec),
            Receivers = receivers.Select(r => SanitizeQueueName(IBMMQMessageReceiver.ToTransportAddress(r.ReceiveAddress))).ToArray()
        });
    }

    static void WriteBrokerDiagnostics(ILog log, StartupDiagnosticEntries diagnostics, MQQueueManager connection)
    {
        try
        {
            diagnostics.Add("IBM MQ broker", new
            {
                QueueManagerName = connection.Name.Trim(),
                connection.CommandLevel
            });
        }
        catch (Exception ex)
        {
            log.Warn("Unable to query IBM MQ broker diagnostics.", ex);
        }
    }

    string SanitizeQueueName(string name) => ResourceNameSanitizer(name);
}
