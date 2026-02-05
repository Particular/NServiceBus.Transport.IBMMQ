using IBM.WMQ;

namespace NServiceBus.Transport.IbmMq;

class IbmMqMessageConverter
{
    public static byte[] FromNative(MQMessage receivedMessage, Dictionary<string, string> messageHeaders, ref string messageId)
    {
        byte[] messageBody = receivedMessage.ReadBytes(receivedMessage.MessageLength);

        // IBM MQ discards empty-string properties entirely. The sender writes a
        // manifest of all header names and a separate list of which ones are empty.
        // Try to read the manifest; if it doesn't exist, fall back to the old approach.
        string manifest = null;
        try
        {
            manifest = receivedMessage.GetStringProperty(IbmMqHelper.HeaderManifestProperty);
        }
        catch (MQException)
        {
            // Manifest property doesn't exist - fall back to GetPropertyNames for backward compatibility
        }

        if (!string.IsNullOrEmpty(manifest))
        {
            // New approach: use manifest to read all headers including empty ones
            string emptyRaw = null;
            try
            {
                emptyRaw = receivedMessage.GetStringProperty(IbmMqHelper.EmptyHeadersProperty);
            }
            catch (MQException)
            {
                // Property not set, which is fine — no empty headers
            }

            var emptySet = new HashSet<string>(
                string.IsNullOrEmpty(emptyRaw) ? Array.Empty<string>() : emptyRaw.Split(','));

            foreach (var escapedName in manifest.Split(','))
            {
                messageHeaders.Add(
                    IbmMqHelper.UnescapePropertyName(escapedName),
                    emptySet.Contains(escapedName) ? "" : receivedMessage.GetStringProperty(escapedName));
            }
        }
        else
        {
            // Old approach: enumerate properties (won't get empty-valued ones, but maintains backward compat)
            var propertyNames = receivedMessage.GetPropertyNames("%");
            while (propertyNames.MoveNext())
            {
                var escapedName = propertyNames.Current.ToString();
                if (escapedName != null)
                {
                    var originalName = IbmMqHelper.UnescapePropertyName(escapedName);
                    messageHeaders.Add(originalName, receivedMessage.GetStringProperty(escapedName));
                }
            }
        }

        // Get message ID from NServiceBus headers, or fall back to native MQ message ID
        messageId = messageHeaders.TryGetValue(Headers.MessageId, out var msgId) && !string.IsNullOrEmpty(msgId)
            ? msgId
            : Convert.ToHexString(receivedMessage.MessageId);

        return messageBody;
    }

    // Delegate to IbmMqHelper which has the correct implementation including:
    // - Empty header manifest handling
    // - Proper property name escaping for all special characters
    // Note: IbmMqHelper needs a QueueManager, but we only use static methods for message creation
    // This is a temporary adapter until we can refactor to pass the QueueManager
    public static MQMessage ToNative(OutgoingMessage outgoingMessage)
    {
        // Temporarily create a message using the same logic as IbmMqHelper.CreateMessage
        // but inline here since we don't have a QueueManager instance
        MQMessage message = new();

        message.MessageType = MQC.MQMT_DATAGRAM;
        message.Persistence = MQC.MQPER_PERSISTENT;
        message.CharacterSet = MQC.CODESET_UTF; // UTF-8

        SetExpiry(outgoingMessage, message);
        SetReplyToQueueName(outgoingMessage, message);
        SetMessageId(outgoingMessage, message);
        SetCorrelationId(outgoingMessage, message);

        // Use IbmMqHelper's property setting logic (includes empty header manifests)
        var pd = new MQPropertyDescriptor { Options = MQC.MQPD_SUPPORT_OPTIONAL };
        var allNames = new List<string>(outgoingMessage.Headers.Count);
        var emptyNames = new List<string>();

        foreach (var header in outgoingMessage.Headers)
        {
            var escapedKey = IbmMqHelper.EscapePropertyName(header.Key);
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

        message.SetStringProperty(IbmMqHelper.HeaderManifestProperty, pd, string.Join(",", allNames));

        if (emptyNames.Count > 0)
        {
            message.SetStringProperty(IbmMqHelper.EmptyHeadersProperty, pd, string.Join(",", emptyNames));
        }

        message.Write(outgoingMessage.Body.ToArray());

        return message;
    }

    static void SetCorrelationId(OutgoingMessage outgoingMessage, MQMessage message)
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

    static void SetMessageId(OutgoingMessage outgoingMessage, MQMessage message)
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

    static void SetReplyToQueueName(OutgoingMessage outgoingMessage, MQMessage message)
    {
        if (outgoingMessage.Headers.TryGetValue(Headers.ReplyToAddress, out var replyToAddress))
        {
            message.ReplyToQueueName = replyToAddress;
        }
    }

    static void SetExpiry(OutgoingMessage outgoingMessage, MQMessage message)
    {
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
}