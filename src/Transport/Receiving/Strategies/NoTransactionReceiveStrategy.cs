namespace NServiceBus.Transport.IBMMQ;

using Logging;

/// <summary>
/// TransactionMode.None: messages are consumed without a syncpoint. If the handler
/// fails and recoverability cannot handle it, the message is lost. This is by design —
/// no-transaction mode trades delivery guarantees for throughput. The base class hooks
/// (OnErrorHandled, OnCancellation) are intentionally no-ops because there is no
/// syncpoint to commit or roll back.
/// </summary>
sealed class NoTransactionReceiveStrategy(
    ILog log,
    MqConnection connection,
    IBMMQMessageConverter converter,
    ReceiveContext context
) : RetryLoopReceiveStrategy(log, connection, converter, context)
{
    public override int GetOptionsFlags => BaseGetOptions;
}
