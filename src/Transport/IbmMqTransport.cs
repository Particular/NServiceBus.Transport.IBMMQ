namespace NServiceBus.Transport.IbmMq;

using IBM.WMQ;
using IBM.WMQ.PCF;
using Logging;

/// <summary>
/// IBM MQ transport for NServiceBus.
/// </summary>
public sealed class IbmMqTransport : TransportDefinition
{
    readonly ILog log = LogManager.GetLogger<IbmMqTransport>();

    IbmMqTransportOptions Options { get; }

    /// <summary>
    /// Creates a new instance of IBM MQ transport with configuration
    /// </summary>
    /// <param name="configure">Configuration object to customize</param>
    public IbmMqTransport(Action<IbmMqTransportOptions> configure)
        : base(TransportTransactionMode.ReceiveOnly, supportsDelayedDelivery: false, supportsPublishSubscribe: true, supportsTTBR: true)
    {
        ArgumentNullException.ThrowIfNull(configure);

        Options = new IbmMqTransportOptions();
        configure(Options);
        new IbmMqTransportOptionsValidate().Validate(Options);
    }

    /// <summary>
    /// Internal constructor for creating transport with pre-configured settings
    /// </summary>
    internal IbmMqTransport(IbmMqTransportOptions options)
        : base(TransportTransactionMode.ReceiveOnly, supportsDelayedDelivery: false, supportsPublishSubscribe: true, supportsTTBR: true)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        new IbmMqTransportOptionsValidate().Validate(Options);
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
        var connectionConfiguration = new ConnectionConfiguration(Options);

        using var setupConnection = new MQQueueManager(Options.QueueManagerName, connectionConfiguration.ConnectionProperties);

        if (hostSettings.SetupInfrastructure)
        {
            foreach (var receiver in receivers)
            {
                var queueName = SanitizeQueueName(IbmMqMessageReceiver.ToTransportAddress(receiver.ReceiveAddress));
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
                var queueName = SanitizeQueueName(IbmMqMessageReceiver.ToTransportAddress(receiver.ReceiveAddress));
                log.DebugFormat("Purging queue {0}", queueName);
                var count = PurgeQueue(setupConnection, queueName);
                log.InfoFormat("Purged {0} messages from queue '{1}'", count, queueName);
            }
        }

        WriteStartupDiagnostics(hostSettings.StartupDiagnostic, Options, connectionConfiguration, receivers, TransportTransactionMode);
        WriteBrokerDiagnostics(log, hostSettings.StartupDiagnostic, setupConnection);

        setupConnection.Disconnect();

        var infrastructure = new IbmMqTransportInfrastructure(log, Options, connectionConfiguration, receivers, TransportTransactionMode, hostSettings.CriticalErrorAction);
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
        IbmMqTransportOptions options,
        ConnectionConfiguration connectionConfiguration,
        ReceiveSettings[] receivers,
        TransportTransactionMode transactionMode)
    {

        diagnostics.Add("IBM MQ transport", new
        {
            TransactionMode = transactionMode.ToString(),
            options.Host,
            options.Port,
            options.Channel,
            Connections = options.Connections.Count > 0 ? string.Join(",", options.Connections) : null,
            options.QueueManagerName,
            connectionConfiguration.ApplicationName,
            Topology = options.Topology?.GetType().ToString(),
            MessageWaitInterval = options.MessageWaitInterval.ToString(),
            options.MaxMessageLength,
            options.CharacterSet,
            SslEnabled = !string.IsNullOrWhiteSpace(options.CipherSpec),
            Receivers = receivers.Select(r => SanitizeQueueName(IbmMqMessageReceiver.ToTransportAddress(r.ReceiveAddress))).ToArray()
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

    string SanitizeQueueName(string name) => Options.ResourceNameSanitizer(name);
}
