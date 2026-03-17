namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
using Logging;

sealed class ReceiveOnlyReceiveStrategy(
    ILog log,
    MqConnection connection,
    IBMMQMessageConverter converter)
    : RetryLoopReceiveStrategy(log, connection, converter)
{
    public override int GetOptionsFlags =>
        MQC.MQGMO_WAIT | MQC.MQGMO_FAIL_IF_QUIESCING | MQC.MQGMO_PROPERTIES_IN_HANDLE | MQC.MQGMO_SYNCPOINT;

    protected override void OnSuccess() => Connection.Commit();
    protected override void OnErrorHandled() => Connection.Commit();
    protected override void OnCriticalFailure() => Connection.Backout();
}
