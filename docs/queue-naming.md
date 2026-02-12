# Queue Naming

Convention is all uppercase! But lowercase is allowed.                               

## Custom Formatter

Override `QueueNameFormatter` when your environment produces queue names that need sanitization or truncation.

```csharp
var transport = new IbmMqTransport(options =>
{
    options.Host = "myhost";
    options.User = "mquser";
    options.Password = "secret";

    options.QueueNameFormatter = name =>
    {
        // Replace hyphens (e.g., from endpoint names like "my-service")
        name = name.Replace('-', '.');

        // Uppercase to follow MQ conventions
        name = name.ToUpperInvariant();

        // Truncate with hash if exceeding 48 characters
        if (name.Length > 48)
        {
            var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(name)))[..8];
            name = $"{name[..(48 - 9)]}_{hash}";
        }

        return name;
    };
});
```

## Convention Comparison

How other NServiceBus transports join address parts:

| Transport | Discriminator separator | Qualifier separator |
|-----------|:-----------------------:|:-------------------:|
| RabbitMQ  | `-`                     | `.`                 |
| Azure Service Bus | `-`             | `.`                 |
| MSMQ      | `-`                     | `.`                 |
| Amazon SQS | `-`                    | `-`                 |
| SQL Server | `.`                    | `.`                 |
| **IBM MQ** | **`.`**                | **`.`**             |

The IBM MQ transport uses `.` for both separators because `-` is not valid in MQ queue names. This differs from the hyphen convention used by most other transports, but `.` is the standard MQ namespace separator.
