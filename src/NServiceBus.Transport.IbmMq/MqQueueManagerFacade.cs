namespace NServiceBus.Transport.IbmMq;

using IBM.WMQ;
using IBM.WMQ.PCF;

class MqQueueManagerFacade(MQQueueManager queueManager, FormatQueueName queueNameFormatter)
{
    public MQQueue AccessSendQueue(string name)
    {
        name = queueNameFormatter(name);
        if (name.Length > 48)
        {
            throw new ArgumentException($"Queue name '{name}' is longer than 48 characters.", nameof(name));
        }

        return queueManager.AccessQueue(name, MQC.MQOO_OUTPUT);
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
        using var destinationQueue = AccessSendQueue(endpointName);

        int finalOptions = options
                           | MQC.MQSO_FAIL_IF_QUIESCING
                           | MQC.MQSO_DURABLE;
        try
        {
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
        }
    }

    static string GenerateTopicName(Type eventType)
    {
        var fullName = (eventType.FullName ?? eventType.Name).Replace('+', '.').ToUpperInvariant();
        var name = $"DEV.{fullName}";
        if (name.Length <= 48)
        {
            return name;
        }

        // Hash-based truncation for names exceeding 48 chars
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(name)))[..8];
        return $"{name[..(48 - 9)]}_{hash}";
    }

    static string GenerateTopicString(Type eventType)
    {
        var fullName = (eventType.FullName ?? eventType.Name).Replace('+', '/').ToLowerInvariant();
        return $"dev/{fullName}/";
    }
}
