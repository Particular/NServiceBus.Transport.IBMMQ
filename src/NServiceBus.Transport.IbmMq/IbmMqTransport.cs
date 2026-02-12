namespace NServiceBus.Transport.IbmMq;

using IBM.WMQ;
using IBM.WMQ.PCF;
using Logging;

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

    public override IReadOnlyCollection<TransportTransactionMode> GetSupportedTransactionModes() =>
    [
        TransportTransactionMode.None,
        TransportTransactionMode.ReceiveOnly,
        TransportTransactionMode.SendsAtomicWithReceive
    ];

    public override Task<TransportInfrastructure> Initialize(
        HostSettings hostSettings,
        ReceiveSettings[] receivers,
        string[] sendingAddresses,
        CancellationToken cancellationToken = default
    )
    {
        var connectionConfiguration = new ConnectionConfiguration(Options);
        FormatQueueName formatter = Options.QueueNameFormatter;

        using var setupConnection = new MQQueueManager(Options.QueueManagerName, connectionConfiguration.ConnectionProperties);

        if (hostSettings.SetupInfrastructure)
        {
            foreach (var receiver in receivers)
            {
                var queueName = formatter(IbmMqMessageReceiver.ToTransportAddress(receiver.ReceiveAddress));
                log.DebugFormat("Creating queue {0}", queueName);
                CreateQueue(setupConnection, queueName);

                if (receiver.ErrorQueue != null)
                {
                    var errorQueueName = formatter(receiver.ErrorQueue);
                    log.DebugFormat("Creating error queue {0}", errorQueueName);
                    CreateQueue(setupConnection, errorQueueName);
                }
            }

            foreach (var sendingAddress in sendingAddresses)
            {
                var queueName = formatter(sendingAddress);
                log.DebugFormat("Creating send queue {0}", queueName);
                CreateQueue(setupConnection, queueName);
            }
        }

        foreach (var receiver in receivers)
        {
            if (receiver.PurgeOnStartup)
            {
                var queueName = formatter(IbmMqMessageReceiver.ToTransportAddress(receiver.ReceiveAddress));
                log.DebugFormat("Purging queue {0}", queueName);
                var count = PurgeQueue(setupConnection, queueName);
                log.InfoFormat("Purged {0} messages from queue '{1}'", count, queueName);
            }
        }

        setupConnection.Disconnect();

        var infrastructure = new IbmMqTransportInfrastructure(log, Options, connectionConfiguration, receivers, TransportTransactionMode, hostSettings.CriticalErrorAction);
        return Task.FromResult<TransportInfrastructure>(infrastructure);
    }

    static void CreateQueue(MQQueueManager queueManager, string name)
    {
        if (name.Length > 48)
        {
            throw new ArgumentException($"Queue name '{name}' is longer than 48 characters.", nameof(name));
        }

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
}
