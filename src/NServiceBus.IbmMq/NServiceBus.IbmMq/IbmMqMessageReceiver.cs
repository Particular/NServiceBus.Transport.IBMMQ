using IBM.WMQ;

namespace NServiceBus.Transport.IbmMq;

internal class IbmMqMessageReceiver : IMessageReceiver
{
    private readonly MQQueueManager _queueManager;
    private readonly ReceiveSettings _receiveSettings;
    private readonly IbmMqHelper _ibmMqHelper;

    OnMessage? onMessage;
    private OnError? onError;
    Task? MessagePump;

    public IbmMqMessageReceiver(MQQueueManager queueManagerInstance, ReceiveSettings receiveSettings)
    {
        _queueManager = queueManagerInstance;
        _receiveSettings = receiveSettings;
        _ibmMqHelper = new IbmMqHelper(queueManagerInstance);
    }

    public ISubscriptionManager Subscriptions => new IbmMqSubscriptionManager(_queueManager, ReceiveAddress);

    public string Id => _receiveSettings.Id;

    public string ReceiveAddress => _receiveSettings.ReceiveAddress.BaseAddress;

    public Task ChangeConcurrency(PushRuntimeSettings limitations, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task Initialize(PushRuntimeSettings limitations, OnMessage onMessage, OnError onError, CancellationToken cancellationToken = default)
    {
        this.onMessage = onMessage;
        this.onError = onError;
        return Task.CompletedTask;
    }

    public async Task StartReceive(CancellationToken cancellationToken = default)
    {
        MessagePump = Task.Run(() => PumpMessages(cancellationToken));
    }

    public Task StopReceive(CancellationToken cancellationToken = default)
        => MessagePump ?? Task.CompletedTask;

    async Task PumpMessages(CancellationToken cancellationToken = default)
    {
        MQQueue queue = _ibmMqHelper.EnsureQueue(ReceiveAddress, MQC.MQOO_INPUT_AS_Q_DEF);

        while (!cancellationToken.IsCancellationRequested)
        {
            MQMessage receivedMessage = new();
            MQGetMessageOptions getOptions = new()
            {
                Options = MQC.MQGMO_WAIT // Should wait for a message to arrive
                    | MQC.MQGMO_SYNCPOINT // Process messages in a transaction (commit/backout)
                    | MQC.MQGMO_FAIL_IF_QUIESCING // Fail if the queue manager is quiescing (shutting down)
                    | MQC.MQGMO_PROPERTIES_IN_HANDLE, // Extract properties from MQRFH2, present body as clean MQSTR

                // TODO: Make WaitInterval configurable
                WaitInterval = 5000 // How long to wait for a message
            };

            string messageId = string.Empty;
            byte[] messageBody = [];
            Dictionary<string, string> messageHeaders = [];

            try
            {
                queue.Get(receivedMessage, getOptions);

                messageBody = receivedMessage.ReadBytes(receivedMessage.MessageLength);

                var propertyNames = receivedMessage.GetPropertyNames("%");
                while (propertyNames.MoveNext())
                {
                    var escapedName = propertyNames.Current.ToString();
                    if (escapedName != null)
                    {
                        // Unescape the property name (restore dots from underscores)
                        var originalName = IbmMqHelper.UnescapePropertyName(escapedName);
                        messageHeaders.Add(originalName, receivedMessage.GetStringProperty(escapedName));
                    }
                }

                if (messageHeaders.TryGetValue(Headers.MessageId, out var messageIdHeader))
                    messageId = messageIdHeader;

                var messageContext = new MessageContext(messageId, messageHeaders, messageBody, new TransportTransaction(), ReceiveAddress, new Extensibility.ContextBag());

                await onMessage!(messageContext, cancellationToken);

                _queueManager.Commit();
            }
            catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
            {
                // Do nothing
                await Task.Yield();
            }
            catch (Exception ex)
            {
                var errorContext = new ErrorContext(ex, messageHeaders, messageId, messageBody, new TransportTransaction(), 0, ReceiveAddress, new Extensibility.ContextBag());

                var result = await onError!.Invoke(errorContext, cancellationToken)
                    .ConfigureAwait(false);

                if (result is ErrorHandleResult.RetryRequired)
                {
                    _queueManager.Backout();
                }
                else

                {
                    _queueManager.Commit();
                }
            }
        }
    }
}
