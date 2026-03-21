namespace NServiceBus.Transport.IBMMQ;

using System.Diagnostics;
using Logging;

abstract class RetryLoopReceiveStrategy(
    ILog log,
    MqConnection connection,
    IBMMQMessageConverter converter,
    ReceiveContext context
) : ReceiveStrategy(connection, converter, log, context)
{
    protected override async ValueTask ProcessReceivedMessage(
        ReceivedMessage msg,
        CancellationToken cancellationToken = default
    )
    {
        var transportTransaction = new TransportTransaction();
        int failureCount = 0;

        // The number of iterations is bounded by the NServiceBus immediate retries
        // configuration (Recoverability.Immediate.NumberOfRetries). Once exhausted,
        // ProcessError returns Handled (moved to error queue) and the loop exits.
        while (true)
        {
            var contextBag = new Extensibility.ContextBag();
            using var attemptActivity = ActivitySources.Main.StartActivity(ActivitySources.Attempt, ActivityKind.Internal);

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
                OnCancellation();
                throw;
            }
            catch (Exception ex)
            {
                failureCount++;
                attemptActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                attemptActivity?.SetTag(ActivitySources.TagFailureCount, failureCount);

                var result = await InvokeOnError(
                    msg,
                    transportTransaction,
                    ex,
                    failureCount,
                    contextBag,
                    cancellationToken
                ).ConfigureAwait(false);

                if (result is ErrorHandleResult.Handled)
                {
                    RecordError(ex, failureCount);
                    OnErrorHandled();
                    return;
                }

                // RetryRequired: loop back for immediate retry
            }
        }
    }

    protected virtual void OnSuccess() { }
    protected virtual void OnErrorHandled() { }
    protected virtual void OnCancellation() { }
}
