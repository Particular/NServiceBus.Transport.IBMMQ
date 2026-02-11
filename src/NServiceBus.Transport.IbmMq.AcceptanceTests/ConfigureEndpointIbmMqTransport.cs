using System;
using System.IO.Hashing;
using System.Text;
using System.Threading.Tasks;
using IBM.WMQ;
using NServiceBus;
using NServiceBus.AcceptanceTesting.Support;
using NServiceBus.Transport.IbmMq;

// ReSharper disable once CheckNamespace
public class ConfigureEndpointIbmMqTransport : IConfigureEndpointTestExecution
{
    static readonly string ConnectionDetails = Environment.GetEnvironmentVariable("IbmMq_ConnectionDetails") ?? "localhost;admin;passw0rd";
    static readonly Hashtable ConnectionProperties = BuildConnectionProperties();

    string endpointName = string.Empty;

    Task IConfigureEndpointTestExecution.Configure(
        string endpointName,
        EndpointConfiguration configuration,
        RunSettings settings,
        PublisherMetadata publisherMetadata
    )
    {
        this.endpointName = endpointName;

        var transport = new IbmMqTransport(s =>
            {
                var parts = ConnectionDetails.Split(';');
                if (parts.Length > 3 && int.TryParse(parts[3], out var port))
                {
                    s.Port = port;
                }
                s.Host = parts[0];
                s.User = parts[1];
                s.Password = parts[2];
                s.MessageWaitInterval = TimeSpan.FromMilliseconds(1);
                s.QueueNameFormatter = Format;
            }
        );

        configuration.UseTransport(transport);

        return Task.CompletedTask;
    }

    Task IConfigureEndpointTestExecution.Cleanup()
    {
        PurgeQueue(Format(endpointName));
        return Task.CompletedTask;
    }

    static string Format(string name)
    {
        if (name.Length <= 48)
        {
            return name;
        }

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var hashHex = Convert.ToHexString(XxHash32.Hash(nameBytes));

        int prefixLength = 48 - hashHex.Length;
        var prefix = name[..Math.Min(prefixLength, name.Length)];
        return $"{prefix}{hashHex}";
    }

    static void PurgeQueue(string queueName)
    {
        try
        {
            using var queueManager = new MQQueueManager("QM1", ConnectionProperties);
            using var queue = queueManager.AccessQueue(queueName, MQC.MQOO_INPUT_AS_Q_DEF);
            var gmo = new MQGetMessageOptions { Options = MQC.MQGMO_NO_WAIT | MQC.MQGMO_ACCEPT_TRUNCATED_MSG };

            while (true)
            {
                try
                {
                    var message = new MQMessage();
                    queue.Get(message, gmo);
                    message.ClearMessage();
                }
                catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
                {
                    break;
                }
            }

            queue.Close();
            queueManager.Disconnect();
        }
        catch (MQException)
        {
            // Queue may not exist, ignore
        }
    }

    static Hashtable BuildConnectionProperties()
    {
        var parts = ConnectionDetails.Split(';');
        var host = parts.Length > 0 ? parts[0] : "localhost";
        var port = parts.Length > 3 && int.TryParse(parts[3], out var p) ? p : 1414;

        return new Hashtable
        {
            { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_MANAGED },
            { MQC.HOST_NAME_PROPERTY, host },
            { MQC.PORT_PROPERTY, port },
            { MQC.CHANNEL_PROPERTY, "DEV.ADMIN.SVRCONN" },
            { MQC.USER_ID_PROPERTY, parts.Length > 1 ? parts[1] : "admin" },
            { MQC.PASSWORD_PROPERTY, parts.Length > 2 ? parts[2] : "passw0rd" }
        };
    }
}
