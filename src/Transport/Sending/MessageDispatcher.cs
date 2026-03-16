namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;

sealed class MessageDispatcher(MqConnectionPool sendPool, TopicTopology topology, IBMMQMessageConverter messageConverter)
    : IMessageDispatcher
{
    public Task Dispatch(TransportOperations outgoingMessages, TransportTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        if (transaction.TryGet<MqConnection>(out var receiveConnection))
        {
            // Sends atomic with receive: dispatch uses the receive connection's SYNCPOINT.
            // Connection-level errors are not handled here — the message pump's reconnect
            // loop owns the lifecycle of this connection and will restart on failure.
            DispatchAtomic(outgoingMessages, receiveConnection);
        }
        else
        {
            DispatchPooled(outgoingMessages);
        }

        return Task.CompletedTask;
    }

    void DispatchPooled(TransportOperations outgoingMessages)
    {
        var conn = sendPool.Rent();
        var putOptions = new MQPutMessageOptions { Options = MQC.MQPMO_FAIL_IF_QUIESCING };

        try
        {
            foreach (var operation in outgoingMessages.UnicastTransportOperations)
            {
                var queue = conn.GetOrOpenSendQueue(operation.Destination);
                var message = messageConverter.ToNative(operation);

                try
                {
                    queue.Put(message, putOptions);
                }
                catch (MQException)
                {
                    conn.EvictQueue(operation.Destination);
                    throw;
                }
            }

            foreach (var operation in outgoingMessages.MulticastTransportOperations)
            {
                foreach (var destination in topology.GetPublishDestinations(operation.MessageType))
                {
                    var topic = conn.GetOrOpenTopic(destination.TopicName, destination.TopicString);
                    var message = messageConverter.ToNative(operation);

                    try
                    {
                        topic.Put(message, putOptions);
                    }
                    catch (MQException)
                    {
                        conn.EvictTopic(destination.TopicName);
                        throw;
                    }
                }
            }

            sendPool.Return(conn);
            conn = null; // prevent discard in catch
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

    void DispatchAtomic(TransportOperations outgoingMessages, MqConnection receiveConn)
    {
        var atomicPutOptions = new MQPutMessageOptions
        {
            Options = MQC.MQPMO_FAIL_IF_QUIESCING | MQC.MQPMO_SYNCPOINT
        };
        var isolatedPutOptions = new MQPutMessageOptions
        {
            Options = MQC.MQPMO_FAIL_IF_QUIESCING
        };

        Dictionary<string, MQQueue>? atomicQueues = null;
        Dictionary<string, MQTopic>? atomicTopics = null;
        MqConnection? isolatedConn = null;

        try
        {
            foreach (var operation in outgoingMessages.UnicastTransportOperations)
            {
                if (operation.RequiredDispatchConsistency == DispatchConsistency.Isolated)
                {
                    isolatedConn ??= sendPool.Rent();
                    var queue = isolatedConn.GetOrOpenSendQueue(operation.Destination);
                    var message = messageConverter.ToNative(operation);
                    queue.Put(message, isolatedPutOptions);
                }
                else
                {
                    atomicQueues ??= [];
                    if (!atomicQueues.TryGetValue(operation.Destination, out var queue))
                    {
                        queue = receiveConn.AccessSendQueue(operation.Destination);
                        atomicQueues[operation.Destination] = queue;
                    }

                    var message = messageConverter.ToNative(operation);
                    queue.Put(message, atomicPutOptions);
                }
            }

            foreach (var operation in outgoingMessages.MulticastTransportOperations)
            {
                foreach (var destination in topology.GetPublishDestinations(operation.MessageType))
                {
                    if (operation.RequiredDispatchConsistency == DispatchConsistency.Isolated)
                    {
                        isolatedConn ??= sendPool.Rent();
                        var topic = isolatedConn.GetOrOpenTopic(destination.TopicName, destination.TopicString);
                        var message = messageConverter.ToNative(operation);
                        topic.Put(message, isolatedPutOptions);
                    }
                    else
                    {
                        atomicTopics ??= [];
                        if (!atomicTopics.TryGetValue(destination.TopicName, out var topic))
                        {
                            topic = receiveConn.EnsureTopic(destination.TopicName, destination.TopicString);
                            atomicTopics[destination.TopicName] = topic;
                        }

                        var message = messageConverter.ToNative(operation);
                        topic.Put(message, atomicPutOptions);
                    }
                }
            }
        }
        finally
        {
            CloseAll(atomicQueues);
            CloseAll(atomicTopics);

            if (isolatedConn != null)
            {
                sendPool.Return(isolatedConn);
            }
        }
    }

    static bool IsConnectionLevelError(MQException ex) =>
        ex.ReasonCode is MQC.MQRC_CONNECTION_BROKEN
            or MQC.MQRC_Q_MGR_NOT_AVAILABLE
            or MQC.MQRC_CONNECTION_QUIESCING
            or MQC.MQRC_CONNECTION_STOPPING;

    static void CloseAll<T>(Dictionary<string, T>? destinations) where T : MQDestination
    {
        if (destinations == null)
        {
            return;
        }

        foreach (var destination in destinations.Values)
        {
            using (destination)
            {
                destination.Close();
            }
        }
    }
}
