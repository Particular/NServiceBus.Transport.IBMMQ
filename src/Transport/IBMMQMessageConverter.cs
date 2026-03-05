namespace NServiceBus.Transport.IBMMQ;

using System.Text.RegularExpressions;
using IBM.WMQ;

static class IBMMQMessageConverter
{
    // IBM MQ silently discards string properties set to "" — they cannot be enumerated via
    // GetPropertyNames nor retrieved via GetStringProperty.  Work around this by:
    //   nsbhdrs  – comma-separated list of all escaped header names
    //   nsbempty – comma-separated list of escaped header names whose value is empty
    // On receive, names in nsbempty are reconstructed as "" without touching MQ properties.
    const string HeaderManifestProperty = "nsbhdrs";
    const string EmptyHeadersProperty = "nsbempty";


    public static byte[] FromNative(MQMessage receivedMessage, Dictionary<string, string> messageHeaders, ref string messageId)
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
                    UnescapePropertyName(escapedName),
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
                    var originalName = UnescapePropertyName(escapedName);
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

    // Delegate to IBMMQHelper which has the correct implementation including:
    // - Empty header manifest handling
    // - Proper property name escaping for all special characters
    // Note: IBMMQHelper needs a QueueManager, but we only use static methods for message creation
    // This is a temporary adapter until we can refactor to pass the QueueManager
    public static MQMessage ToNative(IOutgoingTransportOperation outgoingTransportOperation)
    {
        // Temporarily create a message using the same logic as IBMMQHelper.CreateMessage
        // but inline here since we don't have a QueueManager instance
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

        // Use IBMMQHelper's property setting logic (includes empty header manifests)
        var pd = new MQPropertyDescriptor
        {
            Options = MQC.MQPD_SUPPORT_OPTIONAL
        };
        var allNames = new List<string>(outgoingMessage.Headers.Count);
        var emptyNames = new List<string>();

        foreach (var header in outgoingMessage.Headers)
        {
            var escapedKey = EscapePropertyName(header.Key);
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

    // MQ validates property names as Java identifiers: only ASCII letters, digits, and underscores.
    // Encode underscores as "__", all other non-alphanumeric chars as "_xHHHH".
    // This handles edge cases like:
    // - "Test_xABCD" -> escapes to "Test__xABCD" -> unescapes back to "Test_xABCD" ✓
    // - "Test.Name" -> escapes to "Test_x002EName" -> unescapes back to "Test.Name" ✓
    // - "Test__Value" -> escapes to "Test____Value" -> unescapes back to "Test__Value" ✓
    static string EscapePropertyName(string name)
    {
        // First, escape existing underscores by doubling them
        name = name.Replace("_", "__");

        // Then replace all non-alphanumeric characters (excluding underscore) with _xHHHH
        return Regex.Replace(name, @"[^a-zA-Z0-9_]", match => $"_x{(int)match.Value[0]:X4}");
    }

    static string UnescapePropertyName(string name)
    {
        // First, replace _xHHHH patterns with the corresponding character
        // Use negative lookbehind (?<!_) to avoid matching _x that's part of __x
        // (which represents a literal underscore followed by 'x', not an escape sequence)
        name = Regex.Replace(name, @"(?<!_)_x([0-9A-Fa-f]{4})", match =>
            ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());

        // Then replace double underscores with single underscores
        return name.Replace("__", "_");
    }
}