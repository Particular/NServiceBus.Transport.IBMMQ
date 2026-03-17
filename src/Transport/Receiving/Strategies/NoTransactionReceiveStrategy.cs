namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
using Logging;

sealed class NoTransactionReceiveStrategy(
    ILog log,
    MqConnection connection,
    IBMMQMessageConverter converter
) : RetryLoopReceiveStrategy(log, connection, converter)
{
    public override int GetOptionsFlags =>
        MQC.MQGMO_WAIT | MQC.MQGMO_FAIL_IF_QUIESCING | MQC.MQGMO_PROPERTIES_IN_HANDLE;
}
