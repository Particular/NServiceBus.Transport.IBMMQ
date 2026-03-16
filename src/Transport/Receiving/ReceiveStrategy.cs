namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
using Logging;

abstract class ReceiveStrategy(MqConnection connection, IBMMQMessageConverter messageConverter, ILog log)
{
    readonly bool isDebugEnabled = log.IsDebugEnabled;
    readonly MQMessage receivedMessage = new();

    OnMessage onMessage = null!;

    public MqConnection Connection => connection;

    public void Initialize(
        string queueName, OnMessage onMessage, OnError onError, int workerIndex)
    {
        QueueName = queueName;
        this.onMessage = onMessage;
        OnError = onError;
        WorkerIndex = workerIndex;
    }

    public abstract int GetOptionsFlags { get; }

    protected abstract TransportTransaction CreateTransportTransaction();

    protected abstract Task OnSuccess(CancellationToken cancellationToken = default);

    protected abstract Task OnProcessingFailure(
        Exception exception, string messageId,
        Dictionary<string, string> headers, byte[] body,
        Extensibility.ContextBag contextBag,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Receives and processes one message. Returns false if no message was available.
    /// Throws MQException on connection-level failure (caller should recreate scope).
    /// </summary>
    public async Task<bool> ReceiveMessage(MQQueue queue, MQGetMessageOptions getOptions, CancellationToken cancellationToken = default)
    {
        receivedMessage.ClearMessage();
        receivedMessage.MessageId = MQC.MQMI_NONE;
        receivedMessage.CorrelationId = MQC.MQCI_NONE;

        try
        {
            queue.Get(receivedMessage, getOptions);
        }
        catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
        {
            return false;
        }

        string messageId = string.Empty;
        Dictionary<string, string> messageHeaders = [];
        var messageBody = messageConverter.FromNative(receivedMessage, messageHeaders, ref messageId);
        var originalHeaders = new Dictionary<string, string>(messageHeaders);
        var contextBag = new Extensibility.ContextBag();

        if (isDebugEnabled)
        {
            log.DebugFormat("Worker {0} received message {1}", WorkerIndex, messageId);
        }

        // Check for re-delivered failed message (atomic second-pass)
        if (TryHandleRedeliveredFailure(receivedMessage, messageId, messageBody, originalHeaders,
                out var handlingTask, cancellationToken))
        {
            await handlingTask!.ConfigureAwait(false);
            return true;
        }

        var transportTransaction = CreateTransportTransaction();

        try
        {
            var messageContext = new MessageContext(
                messageId, messageHeaders, messageBody,
                transportTransaction, QueueName, contextBag);

            await onMessage(messageContext, cancellationToken).ConfigureAwait(false);
            await OnSuccess(cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not MQException)
        {
            if (isDebugEnabled)
            {
                log.DebugFormat("Worker {0} error processing message from {1}\n{2}", WorkerIndex, QueueName, ex);
            }

            await OnProcessingFailure(ex, messageId, originalHeaders, messageBody, contextBag, cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
    }

    /// <summary>
    /// Override in AtomicReceiveStrategy to handle the two-pass error flow.
    /// Default: no re-delivery handling (returns false).
    /// </summary>
    protected virtual bool TryHandleRedeliveredFailure(
        MQMessage receivedMessage, string messageId, byte[] messageBody,
        Dictionary<string, string> originalHeaders,
        out Task? handlingTask, CancellationToken cancellationToken = default)
    {
        handlingTask = null;
        return false;
    }

    protected OnError OnError { get; private set; } = null!;
    protected string QueueName { get; private set; } = null!;
    protected int WorkerIndex { get; private set; }
    public Action<string, Exception, CancellationToken>? CriticalError { get; set; }
}
