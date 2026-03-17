namespace NServiceBus.Transport.IBMMQ;

using Logging;

sealed class ReceiveOnlyReceiveStrategy(
    ILog log,
    MqConnection connection,
    IBMMQMessageConverter converter,
    ReceiveContext context)
    : RetryLoopReceiveStrategy(log, connection, converter, context)
{
    public override int GetOptionsFlags => SyncpointGetOptions;

    protected override void OnSuccess() => Connection.Commit();
    protected override void OnErrorHandled() => Connection.Commit();
    protected override void OnCancellation() => Connection.Backout();
}
