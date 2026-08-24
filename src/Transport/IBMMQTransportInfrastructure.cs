namespace NServiceBus.Transport.IBMMQ;

using System.Collections.Concurrent;
using IBM.WMQ;
using Logging;

sealed class IBMMQTransportInfrastructure : TransportInfrastructure, IAsyncDisposable
{
    const int DefaultDestinationCacheCapacity = 100;

    readonly ILog log;
    readonly MqConnectionPool sendPool;
    readonly IBMMQMessageReceiver[] receivers;
    int _disposed;

    public IBMMQTransportInfrastructure(
        ILog log,
        IBMMQTransport transport,
        ConnectionConfiguration connectionConfiguration,
        ReceiveSettings[] receiverSettings,
        TransportTransactionMode transactionMode,
        Action<string, Exception, CancellationToken> criticalError
    )
    {
        ArgumentNullException.ThrowIfNull(connectionConfiguration);
        ArgumentNullException.ThrowIfNull(receiverSettings);

        this.log = log;
        ValidateTransactionMode(transactionMode);

        var queueManagerName = connectionConfiguration.QueueManagerName;
        var connectionProperties = connectionConfiguration.ConnectionProperties;
        var messageWaitInterval = connectionConfiguration.MessageWaitInterval;
        var resourceNameFormatter = transport.ResourceNameSanitizer;
        var characterSet = transport.CharacterSet;
        var circuitBreakerTimeout = transport.TimeToWaitBeforeTriggeringCircuitBreaker;

        MqAdminConnection CreateAdmin() => new(new MQQueueManager(queueManagerName, connectionProperties), resourceNameFormatter);

        // Cache created topics across all connections to avoid redundant admin connection
        // creation. Topic creation is idempotent; the cache prevents repeated attempts for
        // topics that have already been created successfully. GetOrAdd ensures the entry is
        // only stored on success — if CreateTopic throws, the next call will retry.
        var createdTopics = new ConcurrentDictionary<string, byte>();

        void CreateTopic(string topicName, string topicString) =>
            createdTopics.GetOrAdd(topicName, _ =>
            {
                using var admin = CreateAdmin();
                admin.CreateTopic(topicName, topicString);
                return 0;
            });

        var pumpSettings = new MessagePumpSettings(messageWaitInterval);

        MqConnection CreateDataPathConnection() => new(
            LogManager.GetLogger<MqConnection>(),
            new MQQueueManager(queueManagerName, connectionProperties),
            resourceNameFormatter,
            CreateTopic,
            DefaultDestinationCacheCapacity);

        var topology = (TopicTopology)transport.Topology;
        var propertyNameEncoder = new MqPropertyNameEncoder();
        var messageConverter = new IBMMQMessageConverter(propertyNameEncoder, characterSet);
        sendPool = new MqConnectionPool(LogManager.GetLogger<MqConnectionPool>(), CreateDataPathConnection, Environment.ProcessorCount);
        var createAdmin = (CreateMqAdminConnection)CreateAdmin;

        IFailureInfoStorage? failureInfoStorage = null;
        if (transactionMode == TransportTransactionMode.SendsAtomicWithReceive)
        {
            failureInfoStorage = new InMemoryFailureInfoStorage(TimeProvider.System);
        }

        Dispatcher = new MessageDispatcher(sendPool, topology, messageConverter);

        receivers =
        [
            .. receiverSettings.Select(rs =>
            {
                var receiveAddress = resourceNameFormatter(IBMMQMessageReceiver.ToTransportAddress(rs.ReceiveAddress));

                var subscriptionManager = new IBMMQSubscriptionManager(
                    LogManager.GetLogger<IBMMQSubscriptionManager>(),
                    topology,
                    createAdmin,
                    rs.ReceiveAddress.BaseAddress
                );

                var circuitBreaker = new RepeatedFailuresOverTimeCircuitBreaker(
                    $"'{receiveAddress}'",
                    circuitBreakerTimeout,
                    ex => criticalError($"Failed to receive from {receiveAddress}", ex, CancellationToken.None));

                MessagePumpWorker WorkerFactory(string queueName, OnMessage onMessage, OnError onError, int workerIndex)
                {
                    var context = new ReceiveContext(queueName, workerIndex, onMessage, onError, criticalError);
                    var strategyLog = LogManager.GetLogger<ReceiveStrategy>();
                    var strategy = transactionMode switch
                    {
                        TransportTransactionMode.None =>
                            (ReceiveStrategy)new NoTransactionReceiveStrategy(strategyLog, messageConverter, context),
                        TransportTransactionMode.ReceiveOnly =>
                            new ReceiveOnlyReceiveStrategy(strategyLog, messageConverter, context),
                        TransportTransactionMode.SendsAtomicWithReceive =>
                            new AtomicReceiveStrategy(strategyLog, messageConverter, failureInfoStorage!, context),
                        TransportTransactionMode.TransactionScope =>
                            throw new NotSupportedException("TransactionScope is not supported"),
                        _ => throw new ArgumentOutOfRangeException(nameof(transactionMode), transactionMode, "Unsupported transaction mode")
                    };

                    return new MessagePumpWorker(
                        LogManager.GetLogger<MessagePumpWorker>(),
                        strategy, CreateDataPathConnection, pumpSettings, circuitBreaker,
                        queueName, workerIndex);
                }

                return new IBMMQMessageReceiver(
                    LogManager.GetLogger<IBMMQMessageReceiver>(),
                    WorkerFactory, subscriptionManager, circuitBreaker, rs, resourceNameFormatter);
            })
        ];

        Receivers = receivers.ToDictionary(r => r.Id, r => (IMessageReceiver)r);
    }

    public override string ToTransportAddress(QueueAddress address) =>
        IBMMQMessageReceiver.ToTransportAddress(address);

    public override async Task Shutdown(CancellationToken cancellationToken = default)
    {
        log.Debug("Shutdown");
        await DisposeAsync()
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        log.Debug("Disposing");
        try
        {
            foreach (var receiver in receivers)
            {
                await receiver.DisposeAsync()
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            await sendPool.DisposeAsync()
                .ConfigureAwait(false);
        }
    }

    static void ValidateTransactionMode(TransportTransactionMode transactionMode)
    {
        switch (transactionMode)
        {
            case TransportTransactionMode.None:
            case TransportTransactionMode.ReceiveOnly:
            case TransportTransactionMode.SendsAtomicWithReceive:
                return;
            case TransportTransactionMode.TransactionScope:
                throw new NotSupportedException("TransactionScope is not supported");
            default:
                throw new ArgumentOutOfRangeException(nameof(transactionMode), transactionMode, "Unsupported transaction mode");
        }
    }
}
