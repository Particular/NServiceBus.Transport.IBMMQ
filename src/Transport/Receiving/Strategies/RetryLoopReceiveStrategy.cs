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

        // BackoutCount > 0: an earlier delivery did not complete. Count each one as a failed attempt.
        int failureCount = msg.BackoutCount;

        if (failureCount > 0)
        {
            try
            {
                if (await TryHandleFailure(msg, transportTransaction, CreateBackedOutException(msg), failureCount, new Extensibility.ContextBag(), connection, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                OnCancellation(connection);
                throw;
            }
        }

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

                if (await TryHandleFailure(msg, transportTransaction, ex, failureCount, contextBag, connection, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                // RetryRequired: loop back for immediate retry
            }
        }
    }

    async ValueTask<bool> TryHandleFailure(
        ReceivedMessage msg,
        TransportTransaction transportTransaction,
        Exception ex,
        int failureCount,
        Extensibility.ContextBag contextBag,
        MqConnection connection,
        CancellationToken cancellationToken
    )
    {
        var result = await InvokeOnError(
            msg,
            transportTransaction,
            ex,
            failureCount,
            contextBag,
            cancellationToken
        ).ConfigureAwait(false);

        if (result is not ErrorHandleResult.Handled)
        {
            return false;
        }

        RecordError(ex, failureCount);
        OnErrorHandled(connection);
        return true;
    }

    protected virtual void OnSuccess(MqConnection connection) { }
    protected virtual void OnErrorHandled(MqConnection connection) { }
    protected virtual void OnCancellation(MqConnection connection) { }
}
