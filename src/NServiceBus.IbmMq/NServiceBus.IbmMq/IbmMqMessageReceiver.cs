using IBM.WMQ;
using NServiceBus.Logging;

namespace NServiceBus.Transport.IbmMq;

internal class IbmMqMessageReceiver(MQQueueManager queueManagerInstance, ReceiveSettings receiveSettings)
    : IMessageReceiver
{
    readonly ILog Log = LogManager.GetLogger<IbmMqMessageReceiver>();
    private readonly IbmMqHelper _ibmMqHelper = new(queueManagerInstance);
    CancellationTokenSource? messagePumpCts;

    OnMessage? onMessage;
    private OnError? onError;
    Task? MessagePump;

    public ISubscriptionManager Subscriptions => new IbmMqSubscriptionManager(queueManagerInstance, ReceiveAddress);

    public string Id => receiveSettings.Id;

    public string ReceiveAddress => receiveSettings.ReceiveAddress.BaseAddress;

    public Task ChangeConcurrency(PushRuntimeSettings limitations, CancellationToken cancellationToken = default)
    {
        Log.DebugFormat("Changing concurrency to {0}", limitations.MaxConcurrency);
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
        Log.DebugFormat("Starting to receive messages from {0}", ReceiveAddress);
        messagePumpCts = new CancellationTokenSource();
        MessagePump = Task.Run(() => PumpMessages(messagePumpCts.Token), messagePumpCts.Token);
    }

    public Task StopReceive(CancellationToken cancellationToken = default)
    {
        Log.DebugFormat("Stopping to receive messages from {0}", ReceiveAddress);
        messagePumpCts?.Cancel();
        return MessagePump ?? Task.CompletedTask;
    }

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

                queueManagerInstance.Commit();
            }
            catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
            {
                // Do nothing
                await Task.Yield();
            }
            catch (Exception ex)
            {
                Log.DebugFormat("Error processing message from {0}\n{1}", ReceiveAddress, ex);
                var errorContext = new ErrorContext(ex, messageHeaders, messageId, messageBody, new TransportTransaction(), 0, ReceiveAddress, new Extensibility.ContextBag());

                var result = await onError!.Invoke(errorContext, cancellationToken)
                    .ConfigureAwait(false);

                if (result is ErrorHandleResult.RetryRequired)
                {
                    queueManagerInstance.Backout();
                }
                else

                {
                    queueManagerInstance.Commit();
                }
            }
        }
    }
}
