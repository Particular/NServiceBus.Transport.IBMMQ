namespace NServiceBus.Transport.IBMMQ;

using System.Collections.Concurrent;
using System.Text;

/// <summary>
/// Encodes and decodes MQ message property names using the JMS property name encoding standard.
/// </summary>
/// <remarks>
/// <para>
/// MQ message properties must have names that are valid Java identifiers (ASCII letters, digits,
/// and underscores only). The JMS specification defines a standard encoding for property names
/// that contain characters outside this set: underscores are doubled ("__") and all other
/// non-identifier characters are encoded as "_xHHHH" where HHHH is the 4-digit uppercase hex
/// code point.
/// </para>
/// <para>
/// References:
/// - JMS 2.0 Specification, Section 3.5.1 "Message Properties":
///   https://jakarta.ee/specifications/messaging/3.1/jakarta-messaging-spec-3.1#message-properties
/// - IBM MQ property name restrictions:
///   https://www.ibm.com/docs/en/ibm-mq/latest?topic=properties-property-names
/// </para>
/// </remarks>
static class MqPropertyNameEncoder
{
    // The set of distinct header keys in an NServiceBus system is small and fixed: ~15 built-in
    // NServiceBus headers plus a handful of custom headers per application. Even with custom
    // headers, the total number of unique keys is bounded by the application's design, not by
    // message volume. The cache stabilizes after the first few messages and approaches a 100%
    // hit rate, so an eviction policy would add complexity without practical benefit.
    static readonly ConcurrentDictionary<string, string> EncodeCache = new();
    static readonly ConcurrentDictionary<string, string> DecodeCache = new();

    /// <summary>
    /// Encodes a property name for use as an MQ message property.
    /// </summary>
    /// <remarks>
    /// Examples:
    /// - "Test_xABCD" → "Test__xABCD" → decodes back to "Test_xABCD"
    /// - "Test.Name" → "Test_x002EName" → decodes back to "Test.Name"
    /// - "Test__Value" → "Test____Value" → decodes back to "Test__Value"
    /// </remarks>
    public static string Encode(string name) => EncodeCache.GetOrAdd(name, DoEncode);

    /// <summary>
    /// Decodes an MQ property name back to the original property name.
    /// </summary>
    public static string Decode(string name) => DecodeCache.GetOrAdd(name, DoDecode);

    static string DoEncode(string name)
    {
        // Fast path: check if encoding is needed (no underscores and no non-identifier chars)
        var needsEncoding = false;
        foreach (var c in name)
        {
            if (c == '_' || !char.IsAsciiLetterOrDigit(c))
            {
                needsEncoding = true;
                break;
            }
        }

        if (!needsEncoding)
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

    static string DoDecode(string name)
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
