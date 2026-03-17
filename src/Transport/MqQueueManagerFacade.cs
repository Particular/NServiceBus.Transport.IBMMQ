namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;
using IBM.WMQ.PCF;
using Logging;

class MqQueueManagerFacade(
    ILog log,
    MQQueueManager queueManager,
    SanitizeResourceName resourceNameFormatter
    )
{
    readonly DestinationCache<MQQueue> queueCache = new(log, 100);
    readonly DestinationCache<MQTopic> topicCache = new(log, 100);

    public void Disconnect()
    {
        queueCache.Dispose();
        topicCache.Dispose();

        using (queueManager)
        {
            queueManager.Disconnect();
        }
    }

    public MQQueue GetOrOpenSendQueue(string name) => queueCache.GetOrAdd(name, AccessSendQueue);

    public MQTopic GetOrEnsureTopic(string topicName, string topicString) =>
        topicCache.GetOrAdd(topicName, _ => EnsureTopic(topicName, topicString));

    public void EvictSendQueue(string name) => queueCache.Evict(name);

    public void EvictTopic(string name) => topicCache.Evict(name);

    public MQQueue AccessSendQueue(string name)
    {
        var formatted = resourceNameFormatter(name);
        return queueManager.AccessQueue(formatted, MQC.MQOO_OUTPUT);
    }

    public MQTopic AccessTopic(string topicString)
    {
        return queueManager.AccessTopic(
            topicString,
            null,
            MQC.MQTOPIC_OPEN_AS_PUBLICATION,
            MQC.MQOO_OUTPUT
        );
    }

    public MQTopic EnsureTopic(string topicName, string topicString)
    {
        try
        {
            return AccessTopic(topicString);
        }
        catch (MQException)
        {
            // IBM MQ does not return a single distinguishable reason code for
            // "topic object does not exist"; the error depends on queue manager
            // configuration. Optimistically attempt to create the admin object
            // on any failure. CreateTopicOrThrow is idempotent (ignores
            // "already exists") and translates authorization errors into a
            // descriptive exception.
            CreateTopicOrThrow(topicName, topicString);
            return AccessTopic(topicString);
        }
    }

    void CreateTopicOrThrow(string topicName, string topicString)
    {
        try
        {
            CreateTopic(topicName, topicString);
        }
        catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NOT_AUTHORIZED)
        {
            throw new InvalidOperationException(
                $"Topic '{topicName}' does not exist and the current user is not authorized to create it. " +
                "Pre-create topics by running the endpoint with EnableInstallers using an account with administrative permissions, " +
                "or have an MQ administrator create the topic.", ex);
        }
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
