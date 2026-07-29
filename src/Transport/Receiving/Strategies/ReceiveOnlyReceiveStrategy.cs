namespace NServiceBus.Transport.IBMMQ;

using Logging;

sealed class ReceiveOnlyReceiveStrategy(
    ILog log,
    IBMMQMessageConverter converter,
    ReceiveContext context)
    : RetryLoopReceiveStrategy(log, converter, context)
{
    public override int GetOptionsFlags => SyncpointGetOptions;

    protected override void OnSuccess(MqConnection connection) => connection.Commit();
    protected override void OnErrorHandled(MqConnection connection) => connection.Commit();
    protected override void OnCancellation(MqConnection connection) => connection.Backout();
}
