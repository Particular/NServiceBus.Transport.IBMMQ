namespace NServiceBus.Transport.IBMMQ;

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
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

    static readonly MQPropertyDescriptor PropertyDescriptor = new()
    {
        Options = MQC.MQPD_SUPPORT_OPTIONAL
    };


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

            var emptySet = string.IsNullOrEmpty(emptyRaw)
                ? null
                : new HashSet<string>(emptyRaw.Split(','));

            foreach (var escapedName in manifest.Split(','))
            {
                messageHeaders.Add(
                    UnescapePropertyName(escapedName),
                    emptySet != null && emptySet.Contains(escapedName) ? "" : receivedMessage.GetStringProperty(escapedName));
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
                message.SetStringProperty(escapedKey, PropertyDescriptor, header.Value);
            }
        }

        message.SetStringProperty(HeaderManifestProperty, PropertyDescriptor, string.Join(",", allNames));

        if (emptyNames.Count > 0)
        {
            message.SetStringProperty(EmptyHeadersProperty, PropertyDescriptor, string.Join(",", emptyNames));
        }

        WriteBody(outgoingMessage, message);

        return message;
    }

    static void WriteBody(OutgoingMessage outgoingMessage, MQMessage message)
    {
        if (MemoryMarshal.TryGetArray(outgoingMessage.Body, out var segment))
        {
            message.Write(segment.Array!, segment.Offset, segment.Count);
        }
        else
        {
            message.Write(outgoingMessage.Body.ToArray());
        }
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

    // Cache escaped/unescaped property names — NServiceBus reuses the same ~15 header keys
    // for every message, so the cache hit rate approaches 100% after the first message.
    static readonly ConcurrentDictionary<string, string> EscapeNameCache = new();
    static readonly ConcurrentDictionary<string, string> UnescapeNameCache = new();

    // MQ validates property names as Java identifiers: only ASCII letters, digits, and underscores.
    // Encode underscores as "__", all other non-alphanumeric chars as "_xHHHH".
    // This handles edge cases like:
    // - "Test_xABCD" -> escapes to "Test__xABCD" -> unescapes back to "Test_xABCD" ✓
    // - "Test.Name" -> escapes to "Test_x002EName" -> unescapes back to "Test.Name" ✓
    // - "Test__Value" -> escapes to "Test____Value" -> unescapes back to "Test__Value" ✓
    static string EscapePropertyName(string name) => EscapeNameCache.GetOrAdd(name, DoEscapePropertyName);

    static string DoEscapePropertyName(string name)
    {
        // Fast path: check if escaping is needed (no underscores and no non-identifier chars)
        var needsEscape = false;
        foreach (var c in name)
        {
            if (c == '_' || !char.IsAsciiLetterOrDigit(c))
            {
                needsEscape = true;
                break;
            }
        }

        if (!needsEscape)
        {
            return name;
        }

        var sb = new StringBuilder(name.Length + 16);
        foreach (var c in name)
        {
            if (c == '_')
            {
                sb.Append("__");
            }
            else if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else
            {
                sb.Append("_x");
                var hex = (int)c;
                sb.Append(HexChars[(hex >> 12) & 0xF]);
                sb.Append(HexChars[(hex >> 8) & 0xF]);
                sb.Append(HexChars[(hex >> 4) & 0xF]);
                sb.Append(HexChars[hex & 0xF]);
            }
        }

        return sb.ToString();
    }

    static string UnescapePropertyName(string name) => UnescapeNameCache.GetOrAdd(name, DoUnescapePropertyName);

    static string DoUnescapePropertyName(string name)
    {
        // Fast path: if no escape sequences present
        if (name.IndexOf('_') < 0)
        {
            return name;
        }

        var sb = new StringBuilder(name.Length);
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == '_')
            {
                if (i + 1 < name.Length && name[i + 1] == '_')
                {
                    // Double underscore → single underscore
                    sb.Append('_');
                    i++;
                }
                else if (i + 5 < name.Length && name[i + 1] == 'x'
                                              && IsHexDigit(name[i + 2]) && IsHexDigit(name[i + 3])
                                              && IsHexDigit(name[i + 4]) && IsHexDigit(name[i + 5]))
                {
                    // _xHHHH → char
                    var hex = (HexValue(name[i + 2]) << 12)
                              | (HexValue(name[i + 3]) << 8)
                              | (HexValue(name[i + 4]) << 4)
                              | HexValue(name[i + 5]);
                    sb.Append((char)hex);
                    i += 5;
                }
                else
                {
                    sb.Append('_');
                }
            }
            else
            {
                sb.Append(name[i]);
            }
        }

        return sb.ToString();
    }

    const string HexChars = "0123456789ABCDEF";

    static bool IsHexDigit(char c) => c is (>= '0' and <= '9') or (>= 'A' and <= 'F') or (>= 'a' and <= 'f');

    static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'F' => c - 'A' + 10,
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => 0
    };
}
