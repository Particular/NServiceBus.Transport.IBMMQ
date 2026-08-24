namespace NServiceBus.Transport.IBMMQ;

using System.Diagnostics;
using Logging;

abstract class RetryLoopReceiveStrategy(
    ILog log,
    IBMMQMessageConverter converter,
    ReceiveContext context
) : ReceiveStrategy(converter, log, context)
{
    protected override async ValueTask ProcessReceivedMessage(
        ReceivedMessage msg,
        MqConnection connection,
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

                OnSuccess(connection);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                OnCancellation(connection);
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
                    OnErrorHandled(connection);
                    return;
                }

                // RetryRequired: loop back for immediate retry
            }
        }
    }

    protected virtual void OnSuccess(MqConnection connection) { }
    protected virtual void OnErrorHandled(MqConnection connection) { }
    protected virtual void OnCancellation(MqConnection connection) { }
}
