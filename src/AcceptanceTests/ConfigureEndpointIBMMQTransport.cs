using System;
using System.Collections;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IBM.WMQ;
using NServiceBus;
using NServiceBus.AcceptanceTesting.Support;
using Conventions = NServiceBus.AcceptanceTesting.Customization.Conventions;
using NServiceBus.AcceptanceTests.Routing;
using NServiceBus.AcceptanceTests.Routing.NativePublishSubscribe;
using NServiceBus.AcceptanceTests.Sagas;
using NServiceBus.AcceptanceTests.Versioning;
using NServiceBus.Transport.IBMMQ;

// ReSharper disable once CheckNamespace
public class ConfigureEndpointIBMMQTransport : IConfigureEndpointTestExecution
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

        var naming = TestConnectionDetails.CreateTopicNaming();
        var transport = new IBMMQTransport
        {
            MessageWaitInterval = TimeSpan.FromMilliseconds(100),
            TopicNaming = naming,
            ResourceNameSanitizer = Sanitize
        };
        TestConnectionDetails.Apply(transport);

        foreach (var eventType in publisherMetadata.Publishers.SelectMany(p => p.Events))
        {
            var topicString = naming.GenerateTopicString(eventType);
            transport.Topology.SubscribeTo(eventType, topicString);
            transport.Topology.PublishTo(eventType, topicString);
        }

        ApplyMappingsForPolymorphicEvents(endpointName, transport.Topology, naming);

        configuration.UseTransport(transport);

        return Task.CompletedTask;
    }

    static void ApplyMappingsForPolymorphicEvents(string endpointName, ITopicTopology topology, TopicNaming naming)
    {
        if (endpointName == Conventions.EndpointNamingConvention(typeof(MultiSubscribeToPolymorphicEvent.Subscriber)))
        {
            topology.SubscribeTo<MultiSubscribeToPolymorphicEvent.IMyEvent>(naming.GenerateTopicString(typeof(MultiSubscribeToPolymorphicEvent.MyEvent1)));
            topology.SubscribeTo<MultiSubscribeToPolymorphicEvent.IMyEvent>(naming.GenerateTopicString(typeof(MultiSubscribeToPolymorphicEvent.MyEvent2)));
        }

        if (endpointName == Conventions.EndpointNamingConvention(typeof(When_subscribing_to_a_base_event.GeneralSubscriber)))
        {
            topology.SubscribeTo<When_subscribing_to_a_base_event.IBaseEvent>(naming.GenerateTopicString(typeof(When_subscribing_to_a_base_event.SpecificEvent)));
        }

        if (endpointName == Conventions.EndpointNamingConvention(typeof(When_publishing_an_event_implementing_two_unrelated_interfaces.Subscriber)))
        {
            topology.SubscribeTo<When_publishing_an_event_implementing_two_unrelated_interfaces.IEventA>(naming.GenerateTopicString(typeof(When_publishing_an_event_implementing_two_unrelated_interfaces.CompositeEvent)));
            topology.SubscribeTo<When_publishing_an_event_implementing_two_unrelated_interfaces.IEventB>(naming.GenerateTopicString(typeof(When_publishing_an_event_implementing_two_unrelated_interfaces.CompositeEvent)));
        }

        if (endpointName == Conventions.EndpointNamingConvention(typeof(When_started_by_base_event_from_other_saga.SagaThatIsStartedByABaseEvent)))
        {
            topology.SubscribeTo<When_started_by_base_event_from_other_saga.IBaseEvent>(naming.GenerateTopicString(typeof(When_started_by_base_event_from_other_saga.ISomethingHappenedEvent)));
        }

        if (endpointName == Conventions.EndpointNamingConvention(typeof(When_multiple_versions_of_a_message_is_published.V1Subscriber)))
        {
            topology.SubscribeTo<When_multiple_versions_of_a_message_is_published.V1Event>(naming.GenerateTopicString(typeof(When_multiple_versions_of_a_message_is_published.V2Event)));
        }
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
