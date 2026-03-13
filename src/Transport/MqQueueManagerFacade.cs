namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
using IBM.WMQ.PCF;

class MqQueueManagerFacade(MQQueueManager queueManager, SanitizeResourceName resourceNameFormatter)
{
    static readonly Logging.ILog log = Logging.LogManager.GetLogger<MqQueueManagerFacade>();
    readonly Dictionary<string, MQQueue> sendQueueCache = [];
    readonly Dictionary<string, MQTopic> topicCache = [];

    public void Disconnect()
    {
        CloseCachedHandles();
        using (queueManager)
        {
            queueManager.Disconnect();
        }
    }

    public void CloseCachedHandles()
    {
        foreach (var queue in sendQueueCache.Values)
        {
            try
            {
                queue.Close();
            }
            catch (Exception ex)
            {
                log.Info("Failed to close cached queue handle", ex);
            }
        }

        sendQueueCache.Clear();

        foreach (var topic in topicCache.Values)
        {
            try
            {
                topic.Close();
            }
            catch (Exception ex)
            {
                log.Info("Failed to close cached topic handle", ex);
            }
        }

        topicCache.Clear();
    }

    public MQQueue AccessSendQueue(string name)
    {
        if (!sendQueueCache.TryGetValue(name, out var queue))
        {
            var formatted = resourceNameFormatter(name);
            queue = queueManager.AccessQueue(formatted, MQC.MQOO_OUTPUT);
            sendQueueCache[name] = queue;
        }
        return queue;
    }

    public MQTopic AccessTopic(string topicString)
    {
        if (!topicCache.TryGetValue(topicString, out var topic))
        {
            topic = queueManager.AccessTopic(
                topicString,
                null,
                MQC.MQTOPIC_OPEN_AS_PUBLICATION,
                MQC.MQOO_OUTPUT
            );
            topicCache[topicString] = topic;
        }
        return topic;
    }

    public void CreateTopic(string topicName, string topicString)
    {
        var agent = new PCFMessageAgent(queueManager);
        try
        {
            var command = new PCFMessage(MQC.MQCMD_CREATE_TOPIC);
            command.AddParameter(MQC.MQCA_TOPIC_NAME, topicName); // The administrative name of the topic object
            command.AddParameter(MQC.MQCA_TOPIC_STRING, topicString); // The actual topic string used by publishers/subscribers
            agent.Send(command);
        }
        catch (PCFException e) when (e.ReasonCode == MQC.MQRCCF_OBJECT_ALREADY_EXISTS)
        {
            // Topic already exists, nothing to do
        }
        finally
        {
            agent.Disconnect();
        }
    }

    public MQTopic EnsureSubscription(string topicString, string subscriptionName, string endpointName)
    {
        try
        {
            return AccessSubscription(topicString, subscriptionName, endpointName, MQC.MQSO_RESUME);
        }
        catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_SUBSCRIPTION)
        {
            return AccessSubscription(topicString, subscriptionName, endpointName, MQC.MQSO_CREATE);
        }
    }

    public void RemoveSubscription(string subscriptionName)
    {
        var agent = new PCFMessageAgent(queueManager);
        try
        {
            var command = new PCFMessage(MQC.MQCMD_DELETE_SUBSCRIPTION);
            command.AddParameter(MQC.MQCACF_SUB_NAME, subscriptionName);
            agent.Send(command);
        }
        catch (PCFException e) when (e.ReasonCode == MQC.MQRCCF_SUB_NAME_ERROR)
        {
            // Subscription doesn't exist, nothing to remove
        }
        catch (MQException e) when (e.ReasonCode == MQC.MQRC_NOT_AUTHORIZED)
        {
            throw new InvalidOperationException(
                $"Not authorized to delete subscription '{subscriptionName}'. " +
                "Unsubscribe requires administrative permissions. " +
                "Have an MQ administrator remove the subscription.", e);
        }
        finally
        {
            agent.Disconnect();
        }
    }

    MQTopic AccessSubscription(string topicString, string subscriptionName, string endpointName, int options)
    {
        var queueName = resourceNameFormatter(endpointName);

        int finalOptions = options
                           | MQC.MQSO_FAIL_IF_QUIESCING
                           | MQC.MQSO_DURABLE;

        if (options == MQC.MQSO_CREATE)
        {
            // For CREATE, destination queue must be opened with MQOO_OUTPUT per IBM MQ docs
            var destinationQueue = queueManager.AccessQueue(queueName, MQC.MQOO_OUTPUT);
            try
            {
                return queueManager.AccessTopic(
                    destinationQueue,
                    topicString,
                    null,
                    finalOptions,
                    null,
                    subscriptionName
                );
            }
            finally
            {
                destinationQueue.Close();
            }
        }

        // For RESUME/other operations, no destination queue needed
        return queueManager.AccessTopic(
            null,
            topicString,
            null,
            finalOptions,
            null,
            subscriptionName
        );
    }
}
