namespace NServiceBus.PerformanceTests.Infrastructure;

using System.Collections;
using System.IO.Hashing;
using System.Text;
using IBM.WMQ;
using IBM.WMQ.PCF;
using Messages;
using Transport;
using Transport.IBMMQ;

static partial class TestInfrastructure
{
    static readonly IBMMQMessageConverter Converter = new(new MqPropertyNameEncoder());
    static readonly Hashtable ConnectionProperties = BuildConnectionProperties();

    internal static partial string GetTestSuiteName() =>
        "IBM MQ Transport Performance Tests";

    internal static partial string FormatQueueName(string name) =>
        Sanitize(name);

    internal static partial void ConfigureTransport(
        EndpointConfiguration config,
        TransportTransactionMode transactionMode,
        Dictionary<Type, string>? routing)
    {
        var transport = new IBMMQTransport
        {
            MessageWaitInterval = TimeSpan.FromMilliseconds(100),
            TopicNaming = TestConnectionDetails.CreateTopicNaming(),
            ResourceNameSanitizer = Sanitize
        };
        TestConnectionDetails.Apply(transport);

        var routingConfig = config.UseTransport(transport);

        if (routing is { Count: > 0 })
        {
            foreach (var (messageType, destination) in routing)
            {
                routingConfig.RouteToEndpoint(messageType, destination);
            }
        }
    }

    internal static partial void EnsureQueueExists(string queueName, int maxDepth)
    {
        using var queueManager = new MQQueueManager(TestConnectionDetails.QueueManagerName, ConnectionProperties);
        var agent = new PCFMessageAgent(queueManager);
        try
        {
            var request = new PCFMessage(MQC.MQCMD_CREATE_Q);
            request.AddParameter(MQC.MQCA_Q_NAME, queueName);
            request.AddParameter(MQC.MQIA_Q_TYPE, MQC.MQQT_LOCAL);
            request.AddParameter(MQC.MQIA_MAX_Q_DEPTH, maxDepth);
            request.AddParameter(MQC.MQIA_DEF_PERSISTENCE, MQC.MQPER_PERSISTENT);
            agent.Send(request);
        }
        catch (PCFException e) when (e.ReasonCode == MQC.MQRCCF_OBJECT_ALREADY_EXISTS)
        {
            var alter = new PCFMessage(MQC.MQCMD_CHANGE_Q);
            alter.AddParameter(MQC.MQCA_Q_NAME, queueName);
            alter.AddParameter(MQC.MQIA_Q_TYPE, MQC.MQQT_LOCAL);
            alter.AddParameter(MQC.MQIA_MAX_Q_DEPTH, maxDepth);
            agent.Send(alter);
        }
        finally
        {
            agent.Disconnect();
            queueManager.Disconnect();
        }
    }

    internal static partial void PurgeQueue(string queueName)
    {
        using var queueManager = new MQQueueManager(TestConnectionDetails.QueueManagerName, ConnectionProperties);
        var agent = new PCFMessageAgent(queueManager);
        try
        {
            var request = new PCFMessage(MQC.MQCMD_CLEAR_Q);
            request.AddParameter(MQC.MQCA_Q_NAME, queueName);
            agent.Send(request);
        }
        catch (PCFException)
        {
            // Queue may not exist or may already be empty
        }
        finally
        {
            agent.Disconnect();
            queueManager.Disconnect();
        }
    }

    internal static partial void SeedQueue(string queueName, int messageCount) =>
        SeedMessages(queueName, messageCount, typeof(PerfTestMessage), "Send");

    internal static partial void SeedQueueAsEvents(string queueName, int messageCount) =>
        SeedMessages(queueName, messageCount, typeof(PerfTestEvent), "Publish");

    internal static partial void SeedQueueAsFailures(string queueName, int messageCount) =>
        SeedMessages(queueName, messageCount, typeof(PerfTestFailureMessage), "Send");

    static void SeedMessages(string queueName, int messageCount, Type messageType, string intent)
    {
        using var queueManager = new MQQueueManager(TestConnectionDetails.QueueManagerName, ConnectionProperties);
        using var queue = queueManager.AccessQueue(queueName, MQC.MQOO_OUTPUT);
        var pmo = new MQPutMessageOptions { Options = MQC.MQPMO_FAIL_IF_QUIESCING };

        for (int i = 0; i < messageCount; i++)
        {
            var messageId = Guid.NewGuid().ToString();
            var headers = new Dictionary<string, string>
            {
                [Headers.MessageId] = messageId,
                [Headers.EnclosedMessageTypes] = messageType.FullName!,
                [Headers.ContentType] = "application/json",
                [Headers.MessageIntent] = intent
            };

            var body = Encoding.UTF8.GetBytes($"{{\"Index\":{i}}}");
            var outgoingMessage = new OutgoingMessage(messageId, headers, body);
            var operation = new UnicastTransportOperation(outgoingMessage, queueName, []);

            var mqMessage = Converter.ToNative(operation);
            queue.Put(mqMessage, pmo);
        }

        queue.Close();
        queueManager.Disconnect();
    }

    static string Sanitize(string name)
    {
        name = name.Replace('-', '.');

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
