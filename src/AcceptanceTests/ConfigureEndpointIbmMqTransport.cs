using System;
using System.Collections;
using System.IO.Hashing;
using System.Text;
using System.Threading.Tasks;
using IBM.WMQ;
using NServiceBus;
using NServiceBus.AcceptanceTesting.Support;
using NServiceBus.Transport.IBMMQ;

// ReSharper disable once CheckNamespace
public class ConfigureEndpointIbmMqTransport : IConfigureEndpointTestExecution
{
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
                TestConnectionDetails.Apply(s);
                s.MessageWaitInterval = TimeSpan.FromMilliseconds(100);
                s.TopicNaming = TestConnectionDetails.CreateTopicNaming();
                s.ResourceNameSanitizer = Sanitize;
            }
        );

        configuration.UseTransport(transport);

        return Task.CompletedTask;
    }

    Task IConfigureEndpointTestExecution.Cleanup()
    {
        PurgeQueue(Sanitize(endpointName));
        return Task.CompletedTask;
    }

    static string Sanitize(string name)
    {
        name = name
            .Replace('-', '.');

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
            using var queueManager = new MQQueueManager(TestConnectionDetails.QueueManagerName, ConnectionProperties);
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
        return new Hashtable
        {
            { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_MANAGED },
            { MQC.HOST_NAME_PROPERTY, TestConnectionDetails.Host },
            { MQC.PORT_PROPERTY, TestConnectionDetails.Port },
            { MQC.CHANNEL_PROPERTY, TestConnectionDetails.Channel },
            { MQC.USER_ID_PROPERTY, TestConnectionDetails.User },
            { MQC.PASSWORD_PROPERTY, TestConnectionDetails.Password }
        };
    }
}
