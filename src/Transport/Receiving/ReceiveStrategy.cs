namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
using Logging;

readonly record struct ReceivedMessage(string Id, byte[] Body, IReadOnlyDictionary<string, string> Headers);

sealed record ReceiveContext(
    string QueueName,
    int WorkerIndex,
    OnMessage OnMessage,
    OnError OnError,
    Action<string, Exception, CancellationToken> CriticalError);

abstract class ReceiveStrategy(MqConnection connection, IBMMQMessageConverter messageConverter, ILog log, ReceiveContext context)
{
    protected const int BaseGetOptions = MQC.MQGMO_WAIT | MQC.MQGMO_FAIL_IF_QUIESCING | MQC.MQGMO_PROPERTIES_IN_HANDLE;
    protected const int SyncpointGetOptions = BaseGetOptions | MQC.MQGMO_SYNCPOINT;

    public MqConnection Connection => connection;

    public abstract int GetOptionsFlags { get; }

    /// <summary>
    /// Receives and processes one message. Returns false if no message was available.
    /// Throws MQException on connection-level failure (caller should recreate scope).
    /// </summary>
    public async ValueTask<bool> ReceiveMessage(
        MQQueue queue,
        MQGetMessageOptions getOptions,
        CancellationToken cancellationToken = default
    )
    {
        // New MQMessage per receive. ClearMessage() does not reset the property handle
        // used by MQGMO_PROPERTIES_IN_HANDLE, causing stale properties from the previous
        // message to leak through to the next receive. This corrupts raw (non-NServiceBus)
        // messages with headers from a previously received NServiceBus message.
        var receivedMessage = new MQMessage();

        try
        {
            // Blocking call — the IBM MQ managed client has no async API. This runs on a
            // thread pool thread (via Task.Run in MessagePumpWorker.Start) so the impact
            // is bounded by MaxConcurrency workers. Wrapping in another Task.Run would not
            // free a thread — it would just move the block to a different pool thread.
            queue.Get(receivedMessage, getOptions);
        }
        catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
        {
            return false;
        }

        // If cancellation was requested while we were blocked in Get(), bail out
        // without processing. The message stays under syncpoint and is implicitly
        // backed out when the connection scope is disposed. This prevents a race
        // where another worker picks up a message that was backed out during
        // cancellation and re-processes it.
        cancellationToken.ThrowIfCancellationRequested();

        string messageId = string.Empty;
        Dictionary<string, string> messageHeaders = [];
        var messageBody = messageConverter.FromNative(receivedMessage, messageHeaders, ref messageId);

        if (log.IsDebugEnabled)
        {
            log.DebugFormat("Worker {0} received message {1}", context.WorkerIndex, messageId);
        }

        var msg = new ReceivedMessage(messageId, messageBody, messageHeaders);
        await ProcessReceivedMessage(msg, cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    protected abstract ValueTask ProcessReceivedMessage(
        ReceivedMessage msg,
        CancellationToken cancellationToken = default
    );

    protected ValueTask ProcessMessage(
        ReceivedMessage msg, TransportTransaction tx,
        Extensibility.ContextBag ctx, CancellationToken cancellationToken = default) =>
        new(context.OnMessage(CreateMessageContext(msg, tx, ctx), cancellationToken));

    protected ValueTask<ErrorHandleResult> ProcessError(
        ReceivedMessage msg, TransportTransaction tx, Exception ex,
        int failures, Extensibility.ContextBag ctx, CancellationToken cancellationToken = default) =>
        new(context.OnError.Invoke(CreateErrorContext(msg, tx, ex, failures, ctx), cancellationToken));

    protected async ValueTask<ErrorHandleResult> InvokeOnError(
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
            context.CriticalError(
                $"Failed to execute recoverability policy for message with native ID: `{msg.Id}`",
                onErrorEx, cancellationToken);
            return ErrorHandleResult.RetryRequired;
        }
    }

    MessageContext CreateMessageContext(
        ReceivedMessage msg, TransportTransaction tx, Extensibility.ContextBag ctx) =>
        new(msg.Id, new Dictionary<string, string>(msg.Headers), msg.Body, tx, context.QueueName, ctx);

    ErrorContext CreateErrorContext(
        ReceivedMessage msg, TransportTransaction tx, Exception ex,
        int failures, Extensibility.ContextBag ctx) =>
        new(ex, new Dictionary<string, string>(msg.Headers), msg.Id, msg.Body, tx, failures, context.QueueName, ctx);

    protected ILog Log => log;
    protected ReceiveContext Context => context;
}
