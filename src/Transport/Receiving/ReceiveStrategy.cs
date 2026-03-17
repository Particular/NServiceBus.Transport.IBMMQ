namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
using Logging;

record ReceivedMessage(string Id, byte[] Body, IReadOnlyDictionary<string, string> Headers);

abstract class ReceiveStrategy(MqConnection connection, IBMMQMessageConverter messageConverter, ILog log)
{
    readonly MQMessage receivedMessage = new();

    public MqConnection Connection => connection;

    public void Initialize(
        string queueName,
        OnMessage onMessage,
        OnError onError,
        int workerIndex
    )
    {
        QueueName = queueName;
        this.onMessage = onMessage;
        this.onError = onError;
        WorkerIndex = workerIndex;
    }

    public abstract int GetOptionsFlags { get; }

    /// <summary>
    /// Receives and processes one message. Returns false if no message was available.
    /// Throws MQException on connection-level failure (caller should recreate scope).
    /// </summary>
    public async Task<bool> ReceiveMessage(
        MQQueue queue,
        MQGetMessageOptions getOptions,
        CancellationToken cancellationToken = default
    )
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

        if (log.IsDebugEnabled)
        {
            log.DebugFormat("Worker {0} received message {1}", WorkerIndex, messageId);
        }

        var msg = new ReceivedMessage(messageId, messageBody, messageHeaders);
        await ProcessReceivedMessage(msg, cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    protected abstract Task ProcessReceivedMessage(
        ReceivedMessage msg,
        CancellationToken cancellationToken = default
    );

    protected Task ProcessMessage(
        ReceivedMessage msg, TransportTransaction tx,
        Extensibility.ContextBag ctx, CancellationToken cancellationToken = default) =>
        onMessage(CreateMessageContext(msg, tx, ctx), cancellationToken);

    protected Task<ErrorHandleResult> ProcessError(
        ReceivedMessage msg, TransportTransaction tx, Exception ex,
        int failures, Extensibility.ContextBag ctx, CancellationToken cancellationToken = default) =>
        onError.Invoke(CreateErrorContext(msg, tx, ex, failures, ctx), cancellationToken);

    protected async Task<ErrorHandleResult> InvokeOnError(
        ReceivedMessage msg, TransportTransaction tx, Exception ex,
        int failures, Extensibility.ContextBag ctx, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ProcessError(msg, tx, ex, failures, ctx, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception onErrorEx)
        {
            CriticalError?.Invoke(
                $"Failed to execute recoverability policy for message with native ID: `{msg.Id}`",
                onErrorEx, cancellationToken);
            return ErrorHandleResult.RetryRequired;
        }
    }

    MessageContext CreateMessageContext(
        ReceivedMessage msg, TransportTransaction tx, Extensibility.ContextBag ctx) =>
        new(msg.Id, new Dictionary<string, string>(msg.Headers), msg.Body, tx, QueueName, ctx);

    ErrorContext CreateErrorContext(
        ReceivedMessage msg, TransportTransaction tx, Exception ex,
        int failures, Extensibility.ContextBag ctx) =>
        new(ex, new Dictionary<string, string>(msg.Headers), msg.Id, msg.Body, tx, failures, QueueName, ctx);

    OnMessage onMessage = null!;
    OnError onError = null!;
    protected ILog Log => log;
    protected string QueueName { get; private set; } = null!;
    protected int WorkerIndex { get; private set; }
    public Action<string, Exception, CancellationToken>? CriticalError { get; set; }
}
