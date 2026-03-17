namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;

sealed class MessageDispatcher(MqConnectionPool sendPool, TopicTopology topology, IBMMQMessageConverter messageConverter)
    : IMessageDispatcher
{
    const int PutFlags = MQC.MQPMO_FAIL_IF_QUIESCING;
    const int SyncpointPutFlags = MQC.MQPMO_FAIL_IF_QUIESCING | MQC.MQPMO_SYNCPOINT;

    public Task Dispatch(TransportOperations outgoingMessages, TransportTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        if (transaction.TryGet<MqConnection>(out var receiveConn))
        {
            // Sends atomic with receive: dispatch uses the receive connection's SYNCPOINT.
            // Connection-level errors are not handled here — the message pump's reconnect
            // loop owns the lifecycle of this connection and will restart on failure.
            SendAll(receiveConn, outgoingMessages, atomic: true);
        }
        else
        {
            var conn = sendPool.Rent();
            try
            {
                SendAll(conn, outgoingMessages, atomic: false);
                sendPool.Return(conn);
                conn = null;
            }
            catch (MQException ex) when (IsConnectionLevelError(ex))
            {
                if (conn != null)
                {
                    sendPool.Discard(conn);
                }

                throw;
            }
            catch
            {
                if (conn != null)
                {
                    sendPool.Return(conn);
                }

                throw;
            }
        }

        return Task.CompletedTask;
    }

    void SendAll(MqConnection conn, TransportOperations outgoingMessages, bool atomic)
    {
        foreach (var op in outgoingMessages.UnicastTransportOperations)
        {
            conn.PutToQueue(
                op.Destination,
                messageConverter.ToNative(op),
                ResolvePutOptions(atomic, op.RequiredDispatchConsistency)
            );
        }

        foreach (var op in outgoingMessages.MulticastTransportOperations)
        {
            var putOptions = ResolvePutOptions(atomic, op.RequiredDispatchConsistency);

            foreach (var destination in topology.GetPublishDestinations(op.MessageType))
            {
                conn.PutToTopic(
                    destination.TopicName,
                    destination.TopicString,
                    messageConverter.ToNative(op),
                    putOptions);
            }
        }
    }

    static MQPutMessageOptions ResolvePutOptions(bool atomic, DispatchConsistency consistency) =>
        new() { Options = atomic && consistency != DispatchConsistency.Isolated ? SyncpointPutFlags : PutFlags };

    static bool IsConnectionLevelError(MQException ex) =>
        ex.ReasonCode is MQC.MQRC_CONNECTION_BROKEN
            or MQC.MQRC_Q_MGR_NOT_AVAILABLE
            or MQC.MQRC_CONNECTION_QUIESCING
            or MQC.MQRC_CONNECTION_STOPPING;
}
