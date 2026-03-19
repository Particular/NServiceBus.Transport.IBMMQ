namespace NServiceBus.Transport.IBMMQ;

using Logging;

/// <summary>
/// SendsAtomicWithReceive strategy where error sends participate in the same SYNCPOINT
/// as the receive — matching the MSMQ and SQL Server transport approach.
///
/// On the second delivery (after handler failure), onError receives the same
/// TransportTransaction with the receive Connection set. The dispatcher routes error
/// sends (forward to error queue) through the same SYNCPOINT. A single Commit()
/// atomically removes the input message and delivers the error send.
/// </summary>
sealed class AtomicReceiveStrategy(
    ILog log,
    MqConnection connection,
    IBMMQMessageConverter converter,
    IFailureInfoStorage failureInfoStorage,
    ReceiveContext context
) : ReceiveStrategy(connection, converter, log, context)
{
    public override int GetOptionsFlags => SyncpointGetOptions;

    protected override async ValueTask ProcessReceivedMessage(
        ReceivedMessage msg,
        CancellationToken cancellationToken = default
    )
    {
        var transportTransaction = new TransportTransaction();
        transportTransaction.Set(Connection);

        // Use the ContextBag from the failure record on re-delivery so that state set
        // during earlier processing survives into the ErrorContext and any subsequent
        // retry. Core's recoverability pipeline (RecoverabilityPipelineExecutor) uses
        // errorContext.Extensions as the parent ContextBag, and transport tests assert
        // that context items round-trip through the error path. MSMQ and Learning
        // transports follow the same pattern.
        //
        // Trade-off: the stored ContextBag can hold large object graphs alive for the
        // duration of the InMemoryFailureInfoStorage TTL. This is bounded by the short
        // TTL (1 minute) and periodic sweeps.
        failureInfoStorage.TryGetFailureInfo(msg.Id, out var failureRecord);
        var contextBag = failureRecord?.Context ?? new Extensibility.ContextBag();

        try
        {
            if (failureRecord != null)
            {
                if (Log.IsDebugEnabled)
                {
                    Log.DebugFormat("Worker {0} handling previously failed message {1}", Context.WorkerIndex, msg.Id);
                }

                var errorResult = await InvokeOnError(
                    msg,
                    transportTransaction,
                    failureRecord.Exception,
                    failureRecord.NumberOfProcessingAttempts,
                    failureRecord.Context,
                    cancellationToken
                ).ConfigureAwait(false);

                if (errorResult == ErrorHandleResult.Handled)
                {
                    Connection.Commit();
                    failureInfoStorage.ClearFailure(msg.Id);
                    return;
                }

                // RetryRequired: fall through to onMessage below
            }

            await ProcessMessage(
                msg,
                transportTransaction,
                contextBag,
                cancellationToken
            ).ConfigureAwait(false);

            Connection.Commit();
            failureInfoStorage.ClearFailure(msg.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Connection.Backout();
            throw;
        }
        catch (Exception ex)
        {
            failureInfoStorage.RecordFailure(msg.Id, ex, contextBag);
            Connection.Backout();
        }
    }
}
