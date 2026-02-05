using IBM.WMQ;

namespace NServiceBus.Transport.IbmMq;

class IbmMqMessageConverter
{
    public static byte[] FromNative(MQMessage receivedMessage, Dictionary<string, string> messageHeaders, ref string messageId)
    {
        byte[] messageBody;
        messageBody = receivedMessage.ReadBytes(receivedMessage.MessageLength);

        var propertyNames = receivedMessage.GetPropertyNames("%");
        while (propertyNames.MoveNext())
        {
            var escapedName = propertyNames.Current.ToString();
            if (escapedName != null)
            {
                var originalName = UnescapePropertyName(escapedName);
                messageHeaders.Add(originalName, receivedMessage.GetStringProperty(escapedName));
            }
        }

        if (messageHeaders.TryGetValue(Headers.MessageId, out var messageIdHeader))
            messageId = messageIdHeader;
        return messageBody;
    }

    public static MQMessage ToNative(OutgoingMessage outgoingMessage)
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

    static void SetFormatAndCharacterSet(OutgoingMessage outgoingMessage, MQMessage message)
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

    static void SetMessageProperties(OutgoingMessage outgoingMessage, MQMessage message)
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

    static string EscapePropertyName(string name)
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
}