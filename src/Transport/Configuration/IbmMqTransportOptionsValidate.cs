namespace NServiceBus.Transport.IbmMq;

class IbmMqTransportOptionsValidate
{
    /// <summary>
    /// Validates the settings and throws ArgumentException if any are invalid.
    /// </summary>
    public void Validate(IbmMqTransportOptions options)
    {
        // Queue Manager validation - can be null for local default QM
        // No validation needed for QueueManagerName as it can be null

        // Connection validation
        ValidateConnection(options);

        // SSL validation
        ValidateSslConfiguration(options);

        // Message processing validation
        ValidateMessageProcessing(options);

        if (options.Topology == null)
        {
            throw new ArgumentException("Topology must be assigned", nameof(options.Topology));
        }

        if (options.TopicNaming == null)
        {
            throw new ArgumentException("TopicNaming must be assigned", nameof(options.TopicNaming));
        }

        if (options.ResourceNameSanitizer == null)
        {
            throw new ArgumentException("QueueNameFormatter must be assigned", nameof(options.ResourceNameSanitizer));
        }
    }

    void ValidateConnection(IbmMqTransportOptions options)
    {
        // Either ConnectionNameList OR Host+Port must be valid
        if (options.Connections.Count > 0)
        {
            // Validate ConnectionNameList format
            if (!IsValidConnectionNameList(options.Connections))
            {
                throw new ArgumentException(
                    "Connections format is invalid. Expected format: 'host1(port1),host2(port2)'",
                    nameof(options.Connections));
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(options.Host))
            {
                throw new ArgumentException(
                    "Host is required when ConnectionNameList is not specified",
                    nameof(options.Host));
            }

            if (options.Port is <= 0 or > 65535)
            {
                throw new ArgumentException(
                    "Port must be between 1 and 65535",
                    nameof(options.Port));
            }
        }

        if (string.IsNullOrWhiteSpace(options.Channel))
        {
            throw new ArgumentException("Channel is required", nameof(options.Channel));
        }
    }

    void ValidateSslConfiguration(IbmMqTransportOptions options)
    {
        bool hasKeyRepo = !string.IsNullOrWhiteSpace(options.SslKeyRepository);
        bool hasCipherSpec = !string.IsNullOrWhiteSpace(options.CipherSpec);

        // SSL settings must be specified together
        if (hasKeyRepo && !hasCipherSpec)
        {
            throw new ArgumentException(
                "CipherSpec is required when SslKeyRepository is specified",
                nameof(options.CipherSpec));
        }

        if (hasCipherSpec && !hasKeyRepo)
        {
            throw new ArgumentException(
                "SslKeyRepository is required when CipherSpec is specified",
                nameof(options.SslKeyRepository));
        }

        if (options.KeyResetCount < 0)
        {
            throw new ArgumentException(
                "KeyResetCount must be 0 or greater",
                nameof(options.KeyResetCount));
        }
    }

    void ValidateMessageProcessing(IbmMqTransportOptions options)
    {
        if (options.MessageWaitInterval.TotalMilliseconds is < 100 or > 30000)
        {
            throw new ArgumentException(
                "MessageWaitInterval must be between 100 and 30000 milliseconds",
                nameof(options.MessageWaitInterval));
        }

        if (options.MaxMessageLength is < 1024 or > 104857600)
        {
            throw new ArgumentException(
                "MaxMessageLength must be between 1024 (1KB) and 104857600 (100MB) bytes",
                nameof(options.MaxMessageLength));
        }

        if (options.CharacterSet <= 0)
        {
            throw new ArgumentException(
                "CharacterSet must be a positive CCSID value",
                nameof(options.CharacterSet));
        }
    }

    static bool IsValidConnectionNameList(List<string> connectionNameList)
    {
        // Basic validation: should contain at least one entry in format "host(port)"

        if (!connectionNameList.Any())
        {
            return false;
        }

        foreach (var entry in connectionNameList)
        {
            var trimmed = entry.Trim();

            // Must contain opening and closing parentheses
            if (!trimmed.Contains('(') || !trimmed.Contains(')'))
            {
                return false;
            }

            // Extract port to validate it's a number
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