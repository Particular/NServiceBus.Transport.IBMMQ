namespace NServiceBus.Transport.IbmMq;

using IBM.WMQ;
using IBM.WMQ.PCF;

class MqQueueManagerFacade(MQQueueManager queueManager, FormatQueueName queueNameFormatter, string topicPrefix)
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
        var topicName = GenerateTopicName(topicPrefix, eventType);
        var topicString = GenerateTopicString(topicPrefix, eventType);

        MQTopic topic;

        try
        {
            topic = AccessTopic(topicName);
        }
        catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_UNKNOWN_OBJECT_NAME)
        {
            try
            {
                CreateTopic(topicName, topicString);
            }
            catch (MQException createEx) when (createEx.ReasonCode == MQC.MQRC_NOT_AUTHORIZED)
            {
                throw new InvalidOperationException(
                    $"Topic '{topicName}' does not exist and the current user is not authorized to create it. " +
                    "Pre-create topics by running the endpoint with EnableInstallers using an account with administrative permissions, " +
                    "or have an MQ administrator create the topic.", createEx);
            }

            topic = AccessTopic(topicName);
        }

        return topic;
    }

    /// <summary>
    /// Returns all event types in the type hierarchy that should be published to.
    /// This enables polymorphic subscriptions: publishing MyEvent1 : IMyEvent publishes
    /// to both the MyEvent1 and IMyEvent topics so interface subscribers receive the message.
    /// </summary>
    public static IEnumerable<Type> GetEventTypeHierarchy(Type eventType)
    {
        yield return eventType;

        // Walk base classes (excluding object)
        var baseType = eventType.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            yield return baseType;
            baseType = baseType.BaseType;
        }

        // Include all interfaces except NServiceBus marker interfaces
        foreach (var iface in eventType.GetInterfaces())
        {
            if (iface == typeof(IEvent) || iface == typeof(IMessage))
            {
                continue;
            }

            yield return iface;
        }
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
        catch (PCFException e) when (e.ReasonCode == MQC.MQRCCF_OBJECT_ALREADY_EXISTS)
        {
            // Topic already exists, nothing to do
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

    public void RemoveSubscription(Type eventType, string endpointName)
    {
        var subscriptionName = GenerateSubscriptionName(topicPrefix, endpointName, eventType);

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

    MQTopic AccessSubscription(Type eventType, string endpointName, int options)
    {
        var queueName = queueNameFormatter(endpointName);
        var subscriptionName = GenerateSubscriptionName(topicPrefix, endpointName, eventType);

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
                    GenerateTopicString(topicPrefix, eventType),
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
            GenerateTopicString(topicPrefix, eventType),
            null,
            finalOptions,
            null,
            subscriptionName
        );
    }

    internal static string GenerateSubscriptionName(string topicPrefix, string endpointName, Type eventType)
    {
        var topicString = GenerateTopicString(topicPrefix, eventType);
        var name = $"{endpointName}:{topicString}";
        if (name.Length <= 256)
        {
            return name;
        }

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(name)))[..16];
        return $"{name[..(256 - 17)]}_{hash}";
    }

    internal static string GenerateTopicName(string topicPrefix, Type eventType)
    {
        var fullName = (eventType.FullName ?? eventType.Name).Replace('+', '.').ToUpperInvariant();
        var name = $"{topicPrefix.ToUpperInvariant()}.{fullName}";
        if (name.Length <= 48)
        {
            return name;
        }

        // Hash-based truncation for names exceeding 48 chars
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(name)))[..8];
        return $"{name[..(48 - 9)]}_{hash}";
    }

    internal static string GenerateTopicString(string topicPrefix, Type eventType)
    {
        var fullName = (eventType.FullName ?? eventType.Name).Replace('+', '/').ToLowerInvariant();
        return $"{topicPrefix.ToLowerInvariant()}/{fullName}/";
    }
}
