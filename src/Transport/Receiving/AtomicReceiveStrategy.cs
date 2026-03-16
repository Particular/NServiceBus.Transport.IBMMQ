namespace NServiceBus.Transport.IBMMQ;

using System.Collections.Concurrent;
using IBM.WMQ;
using Logging;

sealed class AtomicReceiveStrategy : ReceiveStrategy
{
    ConcurrentDictionary<string, (Exception Exception, Extensibility.ContextBag ContextBag)>? failedMessages;

    public AtomicReceiveStrategy(MqConnection connection, IBMMQMessageConverter converter, ILog log)
        : base(connection, converter, log)
    {
    }

    public void SetFailedMessages(
        ConcurrentDictionary<string, (Exception Exception, Extensibility.ContextBag ContextBag)>? failed) =>
        failedMessages = failed;

    public override int GetOptionsFlags =>
        MQC.MQGMO_WAIT | MQC.MQGMO_FAIL_IF_QUIESCING | MQC.MQGMO_PROPERTIES_IN_HANDLE | MQC.MQGMO_SYNCPOINT;

    protected override TransportTransaction CreateTransportTransaction()
    {
        var tx = new TransportTransaction();
        tx.Set(Connection);
        return tx;
    }

    protected override Task OnSuccess(CancellationToken cancellationToken = default)
    {
        Connection.Commit();
        return Task.CompletedTask;
    }

    protected override async Task OnProcessingFailure(
        Exception exception, string messageId,
        Dictionary<string, string> headers, byte[] body,
        Extensibility.ContextBag contextBag, CancellationToken cancellationToken = default)
    {
        if (failedMessages != null)
        {
            failedMessages[messageId] = (exception, contextBag);
            Connection.Backout();
        }
    }

    /// <summary>
    /// Override to handle the two-pass atomic error flow:
    /// 1st pass: handler fails -> backout (OnProcessingFailure above)
    /// 2nd pass: message re-delivered -> check failedMessages -> invoke onError
    /// </summary>
    protected override bool TryHandleRedeliveredFailure(
        MQMessage receivedMessage, string messageId, byte[] messageBody,
        Dictionary<string, string> originalHeaders,
        out Task? handlingTask, CancellationToken cancellationToken = default)
    {
        if (failedMessages == null || !failedMessages.TryRemove(messageId, out var failedEntry))
        {
            handlingTask = null;
            return false;
        }

        int failures = (receivedMessage.BackoutCount + 1) / 2;

        handlingTask = HandleAtomicOnError(
            failedEntry.Exception, originalHeaders, messageId, messageBody,
            failures, failedEntry.ContextBag, cancellationToken);
        return true;
    }

    async Task HandleAtomicOnError(
        Exception ex, Dictionary<string, string> originalHeaders,
        string messageId, byte[] messageBody, int failures,
        Extensibility.ContextBag contextBag, CancellationToken cancellationToken)
    {
        // Clean transport transaction -- sends from onError go through the pool, not the receive connection
        var errorTransaction = new TransportTransaction();

        var errorContext = new ErrorContext(
            ex, originalHeaders, messageId, messageBody,
            errorTransaction, failures, QueueName, contextBag);

        try
        {
            var result = await OnError.Invoke(errorContext, cancellationToken).ConfigureAwait(false);

            if (result is ErrorHandleResult.RetryRequired)
            {
                Connection.Backout();
            }
            else
            {
                Connection.Commit();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception onErrorEx)
        {
            CriticalError?.Invoke(
                $"Failed to execute recoverability policy for message with native ID: `{messageId}`",
                onErrorEx, cancellationToken);
            Connection.Backout();
        }
    }
}
