namespace NServiceBus.Transport.IbmMq;

using IBM.WMQ;
using IBM.WMQ.PCF;

class IbmMqHelper(MQQueueManager queueManager, Func<string, string>? queueNameFormatter)
{
    public MQQueue EnsureQueue(string name, int openOptions)
    {
        // IBM MQ has a 48-character limit for queue names
        // Truncate long names and add hash for uniqueness
        if (queueNameFormatter != null)
        {
            name = queueNameFormatter(name);
        }

        if (name.Length > 48)
        {
            throw new ArgumentException($"Queue name '{name}' is longer than 48 characters.", nameof(name));
        }

        try
        {
            return AccessQueue(name, openOptions);
        }
        catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_UNKNOWN_OBJECT_NAME)
        {
            return CreateQueue(name, openOptions);
        }
    }

    MQQueue AccessQueue(string name, int openOptions)
    {
        return queueManager.AccessQueue(name, openOptions);
    }

    MQQueue CreateQueue(string name, int openOptions)
    {
        var agent = new PCFMessageAgent(queueManager);
        try
        {
            var request = new PCFMessage(MQC.MQCMD_CREATE_Q);
            request.AddParameter(MQC.MQCA_Q_NAME, name);
            request.AddParameter(MQC.MQIA_Q_TYPE, MQC.MQQT_LOCAL); // Local queue
            request.AddParameter(MQC.MQIA_MAX_Q_DEPTH, 5000); // Max queue depth
            request.AddParameter(MQC.MQIA_DEF_PERSISTENCE, MQC.MQPER_PERSISTENT); // Persistent messages

            agent.Send(request);
        }
        finally
        {
            agent.Disconnect();
        }

        // Try accessing the queue again after creation
        return AccessQueue(name, openOptions);
    }

    public MQTopic EnsureTopic(Type eventType)
    {
        var topicName = GenerateTopicName(eventType);
        var topicString = GenerateTopicString(eventType);

        MQTopic topic;

        try
        {
            topic = AccessTopic(topicName);
        }
        catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_UNKNOWN_OBJECT_NAME)
        {
            CreateTopic(topicName, topicString);

            topic = AccessTopic(topicName);
        }

        return topic;
    }

    MQTopic AccessTopic(string topicName) =>
        queueManager.AccessTopic(
            null,
            topicName,
            MQC.MQTOPIC_OPEN_AS_PUBLICATION,
            MQC.MQOO_OUTPUT
        );

    void CreateTopic(string topicName, string topicString)
    {
        var agent = new PCFMessageAgent(queueManager);
        try
        {
            var command = new PCFMessage(MQC.MQCMD_CREATE_TOPIC);
            command.AddParameter(MQC.MQCA_TOPIC_NAME, topicName); // The administrative name of the topic object
            command.AddParameter(MQC.MQCA_TOPIC_STRING, topicString); // The actual topic string used by publishers/subscribers
            agent.Send(command);
        }
        finally
        {
            agent.Disconnect();
        }
    }

    public MQTopic EnsureSubscription(Type eventType, string endpointName)
    {
        try
        {
            return AccessSubscription(eventType, endpointName, MQC.MQSO_RESUME);
        }
        catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_SUBSCRIPTION)
        {
            return AccessSubscription(eventType, endpointName, MQC.MQSO_CREATE);
        }
    }

    MQTopic AccessSubscription(Type eventType, string endpointName, int options)
    {
        var destinationQueue = EnsureQueue(endpointName, MQC.MQOO_INPUT_SHARED | MQC.MQOO_OUTPUT);
        try
        {
            int finalOptions = options
                               | MQC.MQSO_FAIL_IF_QUIESCING
                               | MQC.MQSO_DURABLE;

            return queueManager.AccessTopic(
                destinationQueue,
                GenerateTopicString(eventType),
                null,
                finalOptions,
                null,
                endpointName
            );
        }
        finally
        {
            destinationQueue.Close();
            ((IDisposable)destinationQueue).Dispose();
        }
    }

    static string GenerateTopicName(Type eventType) => $"DEV.{eventType.Name.ToUpperInvariant()}";

    static string GenerateTopicString(Type eventType) => $"dev/{eventType.Name.ToLowerInvariant()}/";
}