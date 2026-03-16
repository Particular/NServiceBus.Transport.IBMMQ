namespace NServiceBus.Transport.IBMMQ;

using System.Collections.Concurrent;
using IBM.WMQ;
using Logging;

sealed class ReceiveOnlyReceiveStrategy : ReceiveStrategy
{
    ConcurrentDictionary<string, int>? failureCounts;

    public ReceiveOnlyReceiveStrategy(MqConnection connection, IBMMQMessageConverter converter, ILog log)
        : base(connection, converter, log)
    {
    }

    public void SetFailureCounts(ConcurrentDictionary<string, int>? counts) => failureCounts = counts;

    public override int GetOptionsFlags =>
        MQC.MQGMO_WAIT | MQC.MQGMO_FAIL_IF_QUIESCING | MQC.MQGMO_PROPERTIES_IN_HANDLE | MQC.MQGMO_SYNCPOINT;

    protected override TransportTransaction CreateTransportTransaction() => new();

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
        int failures = failureCounts?.AddOrUpdate(messageId, 1, (_, count) => count + 1) ?? 1;

        var errorContext = new ErrorContext(
            exception, headers, messageId, body,
            new TransportTransaction(), failures, QueueName, contextBag);

        try
        {
            failureCounts?.TryRemove(messageId, out _);

            var result = await OnError.Invoke(errorContext, cancellationToken).ConfigureAwait(false);

            if (result is ErrorHandleResult.RetryRequired)
            {
                failureCounts?.TryAdd(messageId, failures);
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
