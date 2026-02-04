using System.Text;
using IBM.WMQ;
using IBM.WMQ.PCF;
using NServiceBus.Transport;

namespace NServiceBus.IbmMq;

internal class IbmMqHelper(MQQueueManager queueManager)
{
    internal MQQueue EnsureQueue(string name, int openOptions)
    {
        try
        {
            return AccessQueue(name, openOptions);
        }
        catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_UNKNOWN_OBJECT_NAME)
        {
            return CreateQueue(name, openOptions);
        }
    }

    private MQQueue AccessQueue(string name, int openOptions)
    {
        return queueManager.AccessQueue(name, openOptions);
    }

    private MQQueue CreateQueue(string name, int openOptions)
    {
        var agent = new PCFMessageAgent(queueManager);

        PCFMessage request = new PCFMessage(MQC.MQCMD_CREATE_Q);
        request.AddParameter(MQC.MQCA_Q_NAME, name);
        request.AddParameter(MQC.MQIA_Q_TYPE, MQC.MQQT_LOCAL); // Local queue
        request.AddParameter(MQC.MQIA_MAX_Q_DEPTH, 5000); // Max queue depth
        request.AddParameter(MQC.MQIA_DEF_PERSISTENCE, MQC.MQPER_PERSISTENT); // Persistent messages

        agent.Send(request); // Send the PCF message to create the queue
        agent.Disconnect(); // Close the agent connection

        // Try accessing the queue again after creation
        return AccessQueue(name, openOptions);
    }

    internal MQMessage CreateMessage(OutgoingMessage outgoingMessage)
    {
        MQMessage message = new();

        //set all MQMD fields first if format is not set, defaults to MQHRF2
        message.MessageType = MQC.MQMT_DATAGRAM;
        message.Persistence = MQC.MQPER_PERSISTENT;
        message.CharacterSet = MQC.CODESET_UTF; // UTF-8

        //set all MQMD fields first if format is not set, defaults to MQHRF2
        /// message.Format = MQC.MQFMT_STRING;
        message.MessageType = MQC.MQMT_DATAGRAM;
        message.Persistence = MQC.MQPER_PERSISTENT;

        message.CharacterSet = 1208; // UTF-8
        SetExpiry(outgoingMessage, message);
        SetReplyToQueueName(outgoingMessage, message);
        SetMessageId(outgoingMessage, message);
        SetCorrelationId(outgoingMessage, message);
       
        SetMessageProperties(outgoingMessage, message);

        message.Write(outgoingMessage.Body.ToArray());

        return message;
    }

    private static void SetMessageProperties(OutgoingMessage outgoingMessage, MQMessage message)
    {
        foreach (var header in outgoingMessage.Headers)
        {
            var pd = new MQPropertyDescriptor();
            pd.Options = MQC.MQPD_SUPPORT_OPTIONAL;
            var escapedKey = EscapePropertyName(header.Key);
            //Console.WriteLine($"Setting/Escaping:{header.Key}->{escapedKey} = {header.Value}");
            message.SetStringProperty(escapedKey, pd, header.Value);
        }
    }

    internal static string EscapePropertyName(string name)
    {
        return name
            //.Replace("_", "_u_")   // Escape underscores FIRST
            //.Replace(".", "_d_")   // Then dots
            .Replace("$", "_dlr_"); // Then dollars
    }

    internal static string UnescapePropertyName(string name)
    {
        return name
            .Replace("_dlr_", "$");  // Unescape in reverse order
            //.Replace("_d_", ".")
            //.Replace("_u_", "_");
    }

    private static void SetCorrelationId(OutgoingMessage outgoingMessage, MQMessage message)
    {
        if (outgoingMessage.Headers.TryGetValue(Headers.CorrelationId, out var correlationId))
        {
            if (Guid.TryParse(correlationId, out var correlationGuid))
            {
                var correlBytes = new byte[24];
                Array.Copy(correlationGuid.ToByteArray(), correlBytes, 16);
                message.CorrelationId = correlBytes;
            }
        }
    }

    private static void SetMessageId(OutgoingMessage outgoingMessage, MQMessage message)
    {
        if (outgoingMessage.Headers.TryGetValue(Headers.MessageId, out var messageId))
        {
            if (Guid.TryParse(messageId, out var messageGuid))
            {
                var messageIdByes = new byte[24];
                Array.Copy(messageGuid.ToByteArray(), messageIdByes, 16);
                message.MessageId = messageIdByes;
            }
        }
    }

    private static void SetReplyToQueueName(OutgoingMessage outgoingMessage, MQMessage message)
    {
        if (outgoingMessage.Headers.TryGetValue(Headers.ReplyToAddress, out var replyToAddress))
        {
            message.ReplyToQueueName = replyToAddress;
        }
    }

    private static void SetExpiry(OutgoingMessage outgoingMessage, MQMessage message)
    {
        // TODO: Maybe this is not set via headers, but message properties, can't remember (RAMON)
        if (outgoingMessage.Headers.TryGetValue(Headers.TimeToBeReceived, out var timeToBeReceived) && !string.IsNullOrEmpty(timeToBeReceived))
        {
            if (TimeSpan.TryParse(timeToBeReceived, out var ttbrValue))
            {
                var expiryInTenthsOfSeconds = (int)(ttbrValue.TotalSeconds * 10);
                message.Expiry = expiryInTenthsOfSeconds;
            }
            else
            {
                throw new InvalidOperationException($"Invalid TimeToBeReceived format: {timeToBeReceived}");
            }
        }
        else
        {
            message.Expiry = MQC.MQEI_UNLIMITED;
        }
    }

    internal MQTopic EnsureTopic(Type eventType)
    {
        var topicName = GenerateTopicName(eventType);
        var topicString = GenerateTopicString(eventType);

        MQTopic topic;

        try
        {
            topic = AccessTopic(topicName, topicString);
        }
        catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_UNKNOWN_OBJECT_NAME)
        {
            CreateTopic(topicName, topicString);

            topic = AccessTopic(topicName, topicString);
        }

        return topic;
    }

    private MQTopic AccessTopic(string topicName, string topicString)
    {
        return queueManager.AccessTopic(
            null,
            topicName,
            MQC.MQTOPIC_OPEN_AS_PUBLICATION,
            MQC.MQOO_OUTPUT);
    }

    private void CreateTopic(string topicName, string topicString)
    {
        var agent = new PCFMessageAgent(queueManager);
        var command = new PCFMessage(MQC.MQCMD_CREATE_TOPIC);
        command.AddParameter(MQC.MQCA_TOPIC_NAME, topicName); // The administrative name of the topic object
        command.AddParameter(MQC.MQCA_TOPIC_STRING, topicString); // The actual topic string used by publishers/subscribers
        agent.Send(command);
    }

    internal MQTopic EnsureSubscription(Type eventType, string endpointName)
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

    private MQTopic AccessSubscription(Type eventType, string endpointName, int options)
    {
        var destinationQueue = EnsureQueue(endpointName, MQC.MQOO_INPUT_SHARED | MQC.MQOO_OUTPUT);

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

    internal static string GenerateTopicName(Type eventType)
    {
        return $"DEV.{eventType.Name.ToUpperInvariant()}";
    }

    internal static string GenerateTopicString(Type eventType)
    {
        return $"dev/{eventType.Name.ToLowerInvariant()}/";
    }
}