namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
using Logging;

sealed class NoTransactionReceiveStrategy(MqConnection connection, IBMMQMessageConverter converter, ILog log)
    : ReceiveStrategy(connection, converter, log)
{
    public override int GetOptionsFlags =>
        MQC.MQGMO_WAIT | MQC.MQGMO_FAIL_IF_QUIESCING | MQC.MQGMO_PROPERTIES_IN_HANDLE;

    protected override TransportTransaction CreateTransportTransaction() => new();

    protected override Task OnSuccess(CancellationToken cancellationToken = default) => Task.CompletedTask;

    protected override async Task OnProcessingFailure(
        Exception exception, string messageId,
        Dictionary<string, string> headers, byte[] body,
        Extensibility.ContextBag contextBag, CancellationToken cancellationToken = default)
    {
        var errorContext = new ErrorContext(
            exception, headers, messageId, body,
            new TransportTransaction(), 1, QueueName, contextBag);

        try
        {
            await OnError.Invoke(errorContext, cancellationToken).ConfigureAwait(false);
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
        }
    }
}
