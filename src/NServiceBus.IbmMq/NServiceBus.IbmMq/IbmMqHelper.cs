using IBM.WMQ;
using IBM.WMQ.PCF;
using NServiceBus.Logging;
using NServiceBus.Transport;
using System.Text;
using System.Text.RegularExpressions;

namespace NServiceBus.Transport.IbmMq;

internal class IbmMqHelper(MQQueueManager queueManager)
{
    internal MQQueue EnsureQueue(string name, int openOptions)
    {
        // IBM MQ has a 48-character limit for queue names
        // Truncate long names and add hash for uniqueness
        if (name.Length > 48)
        {
            var hash = name.GetHashCode().ToString("X8");
            name = name.Substring(0, 48 - 9) + "_" + hash; // 39 chars + "_" + 8 char hash = 48
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
        /// message.Format = MQC.MQFMT_STRING;
        message.MessageType = MQC.MQMT_DATAGRAM;
        message.Persistence = MQC.MQPER_PERSISTENT;
        message.CharacterSet = MQC.CODESET_UTF; // UTF-8

        SetExpiry(outgoingMessage, message);
        SetReplyToQueueName(outgoingMessage, message);
        SetMessageId(outgoingMessage, message);
        SetCorrelationId(outgoingMessage, message);

        SetMessageProperties(outgoingMessage, message);

        message.Write(outgoingMessage.Body.ToArray());

        return message;
    }

    private static void SetFormatAndCharacterSet(OutgoingMessage outgoingMessage, MQMessage message)
    {
        var isTextContentType = outgoingMessage.Headers.TryGetValue(Headers.ContentType, out var contentType)
                                && (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || contentType == "application/json");

       // Log.DebugFormat("ContentType {0} is text {1}", contentType, isTextContentType);

        if (isTextContentType)
        {
            // MQFMT_STRING is set when invoking message.WriteString
            message.Format = MQC.MQFMT_STRING;
            message.CharacterSet = MQC.CODESET_UTF; // UTF-8
        }
        else
        {
            // Payload is non-text, non UTF8, and very likely binary
            message.Format = MQC.MQFMT_NONE;
            message.CharacterSet = MQC.MQCCSI_EMBEDDED;
        }
    }

    // IBM MQ silently discards string properties set to "" — they cannot be enumerated via
    // GetPropertyNames nor retrieved via GetStringProperty.  Work around this by:
    //   nsbhdrs  – comma-separated list of all escaped header names
    //   nsbempty – comma-separated list of escaped header names whose value is empty
    // On receive, names in nsbempty are reconstructed as "" without touching MQ properties.
    internal const string HeaderManifestProperty = "nsbhdrs";
    internal const string EmptyHeadersProperty   = "nsbempty";

    private static void SetMessageProperties(OutgoingMessage outgoingMessage, MQMessage message)
    {
        var pd = new MQPropertyDescriptor { Options = MQC.MQPD_SUPPORT_OPTIONAL };
        var allNames   = new List<string>(outgoingMessage.Headers.Count);
        var emptyNames = new List<string>();

        foreach (var header in outgoingMessage.Headers)
        {
            var escapedKey = EscapePropertyName(header.Key);
            allNames.Add(escapedKey);

            if (header.Value.Length == 0)
            {
                emptyNames.Add(escapedKey);
            }
            else
            {
                message.SetStringProperty(escapedKey, pd, header.Value);
            }
        }

        message.SetStringProperty(HeaderManifestProperty, pd, string.Join(",", allNames));

        if (emptyNames.Count > 0)
        {
            message.SetStringProperty(EmptyHeadersProperty, pd, string.Join(",", emptyNames));
        }
    }

    // MQ validates property names as Java identifiers: only ASCII letters, digits, and underscores.
    // Encode underscores as "__", all other non-alphanumeric chars as "_xHHHH".
    // This handles edge cases like:
    // - "Test_xABCD" -> escapes to "Test__xABCD" -> unescapes back to "Test_xABCD" ✓
    // - "Test.Name" -> escapes to "Test_x002EName" -> unescapes back to "Test.Name" ✓
    // - "Test__Value" -> escapes to "Test____Value" -> unescapes back to "Test__Value" ✓    
    internal static string EscapePropertyName(string name)
    {
        // First, escape existing underscores by doubling them
        name = name.Replace("_", "__");

        // Then replace all non-alphanumeric characters (excluding underscore) with _xHHHH
        return Regex.Replace(name, @"[^a-zA-Z0-9_]", match => $"_x{(int)match.Value[0]:X4}");
    }

    internal static string UnescapePropertyName(string name)
    {
        // First, replace _xHHHH patterns with the corresponding character
        // Use negative lookbehind (?<!_) to avoid matching _x that's part of __x
        // (which represents a literal underscore followed by 'x', not an escape sequence)
        name = Regex.Replace(name, @"(?<!_)_x([0-9A-Fa-f]{4})", match =>
            ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());

        // Then replace double underscores with single underscores
        return name.Replace("__", "_");
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

    MQTopic AccessTopic(string topicName, string topicString)
    {
        return queueManager.AccessTopic(
            null,
            topicName,
            MQC.MQTOPIC_OPEN_AS_PUBLICATION,
            MQC.MQOO_OUTPUT);
    }

    void CreateTopic(string topicName, string topicString)
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

    MQTopic AccessSubscription(Type eventType, string endpointName, int options)
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

    static string GenerateTopicName(Type eventType)
    {
        return $"DEV.{eventType.Name.ToUpperInvariant()}";
    }

    static string GenerateTopicString(Type eventType)
    {
        return $"dev/{eventType.Name.ToLowerInvariant()}/";
    }
}