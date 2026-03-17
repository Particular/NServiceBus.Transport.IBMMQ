namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;

sealed class MessageDispatcher(MqConnectionPool sendPool, TopicTopology topology, IBMMQMessageConverter messageConverter)
    : IMessageDispatcher
{
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
            var conn = sendPool.Rent(cancellationToken);
            try
            {
                SendAll(conn, outgoingMessages, atomic: false);
                sendPool.Return(conn);
                conn = null;
            }
            catch (MQException ex) when (IsConnectionLevelError(ex))
            {
                // MQException with a connection-level reason code means the connection
                // is dirty/broken — discard it rather than returning it to the pool.
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
                CreatePutOptions(syncpoint: atomic && op.RequiredDispatchConsistency != DispatchConsistency.Isolated)
            );
        }

        foreach (var op in outgoingMessages.MulticastTransportOperations)
        {
            var syncpoint = atomic && op.RequiredDispatchConsistency != DispatchConsistency.Isolated;

            foreach (var destination in topology.GetPublishDestinations(op.MessageType))
            {
                conn.PutToTopic(
                    destination.TopicName,
                    destination.TopicString,
                    messageConverter.ToNative(op),
                    CreatePutOptions(syncpoint));
            }
        }
    }

    static MQPutMessageOptions CreatePutOptions(bool syncpoint) => new()
    {
        // MQPutMessageOptions is mutable — the MQ client writes back fields (ResolvedQueueName,
        // CompletionCode, ReasonCode, etc.) after each Put() call, so a fresh instance is needed per Put.
        Options = MQC.MQPMO_FAIL_IF_QUIESCING | (syncpoint ? MQC.MQPMO_SYNCPOINT : 0)
    };

    static bool IsConnectionLevelError(MQException ex) =>
        ex.ReasonCode is MQC.MQRC_CONNECTION_BROKEN
            or MQC.MQRC_Q_MGR_NOT_AVAILABLE
            or MQC.MQRC_CONNECTION_QUIESCING
            or MQC.MQRC_CONNECTION_STOPPING;
}
