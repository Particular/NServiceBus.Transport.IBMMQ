namespace NServiceBus.Transport.IBMMQ;

using IBM.WMQ;

class IBMMQMessageConverter(MqPropertyNameEncoder propertyNameEncoder)
{
    // IBM MQ silently discards string properties set to "" — they cannot be enumerated via
    // GetPropertyNames nor retrieved via GetStringProperty.  Work around this by:
    //   nsbhdrs  – comma-separated list of all escaped header names
    //   nsbempty – comma-separated list of escaped header names whose value is empty
    // On receive, names in nsbempty are reconstructed as "" without touching MQ properties.
    const string HeaderManifestProperty = "nsbhdrs";
    const string EmptyHeadersProperty = "nsbempty";


    public byte[] FromNative(MQMessage receivedMessage, Dictionary<string, string> messageHeaders, ref string messageId)
    {
        byte[] messageBody = receivedMessage.ReadBytes(receivedMessage.MessageLength);

        // IBM MQ discards empty-string properties entirely. The sender writes a
        // manifest of all header names and a separate list of which ones are empty.
        // Try to read the manifest; if it doesn't exist, fall back to the old approach.
        string? manifest = null;
        try
        {
            manifest = receivedMessage.GetStringProperty(HeaderManifestProperty);
        }
        catch (MQException)
        {
            // Manifest property doesn't exist - fall back to GetPropertyNames for backward compatibility
        }

        if (!string.IsNullOrEmpty(manifest))
        {
            // New approach: use manifest to read all headers including empty ones
            string? emptyRaw = null;
            try
            {
                emptyRaw = receivedMessage.GetStringProperty(EmptyHeadersProperty);
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
                    propertyNameEncoder.Decode(escapedName),
                    emptySet.Contains(escapedName) ? "" : receivedMessage.GetStringProperty(escapedName));
            }
        }
        else
        {
            // Old approach: enumerate properties (won't get empty-valued ones, but maintains backward compat)
            var propertyNames = receivedMessage.GetPropertyNames("%");
            while (propertyNames.MoveNext())
            {
                var escapedName = propertyNames.Current!.ToString();
                if (escapedName != null)
                {
                    var originalName = propertyNameEncoder.Decode(escapedName);
                    messageHeaders.Add(originalName, receivedMessage.GetStringProperty(escapedName));
                }
            }
        }

        // Lift native MQ message properties to headers when no header value already exists.
        // This enables interoperability with non-NServiceBus senders that set native MQ
        // fields directly instead of encoding them as named string properties.
        LiftNativeProperties(receivedMessage, messageHeaders);

        // Get message ID from NServiceBus headers, or fall back to native MQ message ID
        messageId = messageHeaders.TryGetValue(Headers.MessageId, out var msgId) && !string.IsNullOrEmpty(msgId)
            ? msgId
            : Convert.ToHexString(receivedMessage.MessageId);

        return messageBody;
    }

    static void LiftNativeProperties(MQMessage message, Dictionary<string, string> headers)
    {
        if (!headers.ContainsKey(Headers.MessageId) && !IsEmpty(message.MessageId))
        {
            headers[Headers.MessageId] = Convert.ToHexString(message.MessageId);
        }

        if (!headers.ContainsKey(Headers.CorrelationId) && !IsEmpty(message.CorrelationId))
        {
            headers[Headers.CorrelationId] = Convert.ToHexString(message.CorrelationId);
        }

        if (!headers.ContainsKey(Headers.ReplyToAddress))
        {
            var replyTo = message.ReplyToQueueName?.Trim();
            if (!string.IsNullOrEmpty(replyTo))
            {
                headers[Headers.ReplyToAddress] = replyTo;
            }
        }

        if (!headers.ContainsKey(Headers.NonDurableMessage) && message.Persistence == MQC.MQPER_NOT_PERSISTENT)
        {
            headers[Headers.NonDurableMessage] = true.ToString();
        }

        if (!headers.ContainsKey(Headers.TimeToBeReceived) && message.Expiry != MQC.MQEI_UNLIMITED)
        {
            headers[Headers.TimeToBeReceived] = TimeSpan.FromSeconds(message.Expiry / 10.0).ToString();
        }
    }

    static bool IsEmpty(byte[]? bytes) => bytes is null || Array.TrueForAll(bytes, static b => b == 0);

    /// Always creates a new MQMessage — do not attempt to reuse MQMessage instances
    /// because ClearMessage does not fully reset all properties (see MqMessageClearBehaviorTests).
    public MQMessage ToNative(IOutgoingTransportOperation outgoingTransportOperation)
    {
        var isNonDurable = outgoingTransportOperation.Message.Headers.ContainsKey(Headers.NonDurableMessage);

        MQMessage message = new()
        {
            MessageType = MQC.MQMT_DATAGRAM,
            Persistence = isNonDurable ? MQC.MQPER_NOT_PERSISTENT : MQC.MQPER_PERSISTENT,
            CharacterSet = MQC.CODESET_UTF // UTF-8
        };

        var outgoingMessage = outgoingTransportOperation.Message;
        SetExpiry(outgoingTransportOperation, message);
        SetReplyToQueueName(outgoingMessage, message);
        SetMessageId(outgoingMessage, message);
        SetCorrelationId(outgoingMessage, message);

        var pd = new MQPropertyDescriptor
        {
            Options = MQC.MQPD_SUPPORT_OPTIONAL
        };
        var allNames = new List<string>(outgoingMessage.Headers.Count);
        var emptyNames = new List<string>();

        foreach (var header in outgoingMessage.Headers)
        {
            var escapedKey = propertyNameEncoder.Encode(header.Key);
            allNames.Add(escapedKey);

            if (string.IsNullOrEmpty(header.Value))
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

        message.Write(outgoingMessage.Body.ToArray());

        return message;
    }

    static void SetCorrelationId(OutgoingMessage outgoingMessage, MQMessage message)
    {
        if (outgoingMessage.Headers.TryGetValue(Headers.CorrelationId, out var correlationId)
            && TryConvertToMqId(correlationId, out var correlBytes))
        {
            message.CorrelationId = correlBytes;
        }
    }

    static void SetMessageId(OutgoingMessage outgoingMessage, MQMessage message)
    {
        if (outgoingMessage.Headers.TryGetValue(Headers.MessageId, out var messageId)
            && TryConvertToMqId(messageId, out var idBytes))
        {
            message.MessageId = idBytes;
        }
    }

    static bool TryConvertToMqId(string? value, out byte[] mqId)
    {
        mqId = new byte[24];

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (Guid.TryParse(value, out var guid))
        {
            Array.Copy(guid.ToByteArray(), mqId, 16);
            return true;
        }

        try
        {
            var bytes = Convert.FromHexString(value);
            if (bytes.Length <= 24)
            {
                Array.Copy(bytes, mqId, bytes.Length);
                return true;
            }
        }
        catch (FormatException)
        {
            // Not a valid hex string
        }

        return false;
    }

    static void SetReplyToQueueName(OutgoingMessage outgoingMessage, MQMessage message)
    {
        if (outgoingMessage.Headers.TryGetValue(Headers.ReplyToAddress, out var replyToAddress))
        {
            message.ReplyToQueueName = replyToAddress;
        }
    }

    static void SetExpiry(IOutgoingTransportOperation outgoingTransportOperation, MQMessage message)
    {
        if (outgoingTransportOperation?.Properties?.DiscardIfNotReceivedBefore != null)
        {
            var ttbrValue = outgoingTransportOperation.Properties.DiscardIfNotReceivedBefore.MaxTime;
            var expiryInTenthsOfSeconds = (int)(ttbrValue.TotalSeconds * 10);
            message.Expiry = expiryInTenthsOfSeconds;
        }
        else
        {
            message.Expiry = MQC.MQEI_UNLIMITED;
        }
    }
}