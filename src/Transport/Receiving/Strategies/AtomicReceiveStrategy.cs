namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
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
    IFailureInfoStorage failureInfoStorage
) : ReceiveStrategy(connection, converter, log)
{
    public override int GetOptionsFlags =>
        MQC.MQGMO_WAIT | MQC.MQGMO_FAIL_IF_QUIESCING | MQC.MQGMO_PROPERTIES_IN_HANDLE | MQC.MQGMO_SYNCPOINT;

    protected override async Task ProcessReceivedMessage(
        ReceivedMessage msg,
        CancellationToken cancellationToken = default
    )
    {
        var transportTransaction = new TransportTransaction();
        transportTransaction.Set(Connection);

        if (failureInfoStorage.TryGetFailureInfo(msg.Id, out var failureRecord))
        {
            if (Log.IsDebugEnabled)
            {
                Log.DebugFormat("Worker {0} handling previously failed message {1}", WorkerIndex, msg.Id);
            }

            var errorResult = await InvokeOnError(
                msg,
                transportTransaction,
                failureRecord!.Exception,
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

        var contextBag = new Extensibility.ContextBag();

        try
        {
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
            throw;
        }
        catch (Exception ex)
        {
            failureInfoStorage.RecordFailure(msg.Id, ex, contextBag);
            Connection.Backout();
        }
    }
}
