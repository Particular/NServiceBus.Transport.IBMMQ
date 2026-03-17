namespace NServiceBus.Transport.IBMMQ;

using Logging;

abstract class RetryLoopReceiveStrategy(
    ILog log,
    MqConnection connection,
    IBMMQMessageConverter converter
) : ReceiveStrategy(connection, converter, log)
{
    protected override async Task ProcessReceivedMessage(
        ReceivedMessage msg,
        CancellationToken cancellationToken = default
    )
    {
        var transportTransaction = new TransportTransaction();
        int failureCount = 0;

        while (true)
        {
            var contextBag = new Extensibility.ContextBag();

            try
            {
                await ProcessMessage(
                    msg,
                    transportTransaction,
                    contextBag,
                    cancellationToken
                ).ConfigureAwait(false);

                OnSuccess();
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failureCount++;

                try
                {
                    var result = await ProcessError(
                        msg,
                        transportTransaction,
                        ex,
                        failureCount,
                        contextBag,
                        cancellationToken
                    ).ConfigureAwait(false);

                    if (result is ErrorHandleResult.Handled)
                    {
                        OnErrorHandled();
                        return;
                    }

                    // RetryRequired: loop back for immediate retry
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
                    OnCriticalFailure();
                    return;
                }
            }
        }
    }

    protected virtual void OnSuccess() { }
    protected virtual void OnErrorHandled() { }
    protected virtual void OnCriticalFailure() { }
}
