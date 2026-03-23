namespace NServiceBus.Transport.IBMMQ;

static class IBMMQTransportValidator
{
    public static void Validate(IBMMQTransport transport)
    {
        ValidateConnection(transport);
        ValidateSslConfiguration(transport);
    }

    static void ValidateConnection(IBMMQTransport transport)
    {
        if (transport.Connections.Count > 0)
        {
            if (!IsValidConnectionNameList(transport.Connections))
            {
                throw new ArgumentException(
                    "Connections format is invalid. Expected format: 'host1(port1),host2(port2)'",
                    nameof(transport.Connections));
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(transport.Host))
            {
                throw new ArgumentException(
                    "Host is required when ConnectionNameList is not specified",
                    nameof(transport.Host));
            }
        }

        if (string.IsNullOrWhiteSpace(transport.Channel))
        {
            throw new ArgumentException("Channel is required", nameof(transport.Channel));
        }
    }

    static void ValidateSslConfiguration(IBMMQTransport transport)
    {
        bool hasKeyRepo = !string.IsNullOrWhiteSpace(transport.SslKeyRepository);
        bool hasCipherSpec = !string.IsNullOrWhiteSpace(transport.CipherSpec);

        if (hasKeyRepo && !hasCipherSpec)
        {
            throw new ArgumentException(
                "CipherSpec is required when SslKeyRepository is specified",
                nameof(transport.CipherSpec));
        }

        if (hasCipherSpec && !hasKeyRepo)
        {
            throw new ArgumentException(
                "SslKeyRepository is required when CipherSpec is specified",
                nameof(transport.SslKeyRepository));
        }
    }

    static bool IsValidConnectionNameList(List<string> connectionNameList)
    {
        if (!connectionNameList.Any())
        {
            return false;
        }

        foreach (var entry in connectionNameList)
        {
            var trimmed = entry.Trim();

            if (!trimmed.Contains('(') || !trimmed.Contains(')'))
            {
                return false;
            }

            var startParen = trimmed.IndexOf('(');
            var endParen = trimmed.IndexOf(')');

            if (startParen >= endParen || startParen == 0)
            {
                return false;
            }

            var portStr = trimmed.Substring(startParen + 1, endParen - startParen - 1);

            if (!int.TryParse(portStr, out var port) || port <= 0 || port > 65535)
            {
                return false;
            }
        }

        return true;
    }
}
