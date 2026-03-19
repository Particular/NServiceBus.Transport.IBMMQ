namespace NServiceBus.Transport.IBMMQ;

sealed record FailureRecord(int NumberOfProcessingAttempts, Exception Exception, Extensibility.ContextBag Context);

interface IFailureInfoStorage
{
    void RecordFailure(string messageId, Exception exception, Extensibility.ContextBag context);
    bool TryGetFailureInfo(string messageId, out FailureRecord? info);
    void ClearFailure(string messageId);
}
