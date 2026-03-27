namespace NServiceBus.Transport.IBMMQ.Tests.Topology;

using System;
using System.Linq;
using IBM.WMQ;
using IBM.WMQ.PCF;
using NUnit.Framework;

[TestFixture]
[Category("RequiresBroker")]
public class TopologyCreatorTests
{
    static readonly NServiceBus.Logging.ILog log = NServiceBus.Logging.LogManager.GetLogger<TopologyCreatorTests>();
    readonly TopicNaming naming = new ShortenedTopicNaming("TEST");

    [OneTimeSetUp]
    public void Setup() => BrokerRequirement.Verify();

    [TearDown]
    public void Cleanup()
    {
        using var qm = TestBrokerConnection.Connect();
        DeleteTopicIfExists(qm, "TEST.TOPOCREATOR.A");
        DeleteTopicIfExists(qm, "TEST.TOPOCREATOR.B");
        qm.Disconnect();
    }

    [Test]
    public void Create_creates_topic_with_explicit_admin_name()
    {
        var topology = new TopicTopology { Naming = naming };
        topology.PublishTo(typeof(ConcreteEvent), "TEST.TOPOCREATOR.A", "test/topocreator/a/");

        using var qm = TestBrokerConnection.Connect();
        var admin = new MqAdminConnection(qm, s => s);
        var creator = new TopologyCreator(log, topology, admin);

        creator.Create();

        Assert.That(TopicAdminObjectExists(qm, "TEST.TOPOCREATOR.A"), Is.True);
    }

    [Test]
    public void Create_creates_multiple_topics()
    {
        var topology = new TopicTopology { Naming = naming };
        topology.PublishTo(typeof(ConcreteEvent), "TEST.TOPOCREATOR.A", "test/topocreator/a/");
        topology.PublishTo(typeof(BaseEvent), "TEST.TOPOCREATOR.B", "test/topocreator/b/");

        using var qm = TestBrokerConnection.Connect();
        var admin = new MqAdminConnection(qm, s => s);
        var creator = new TopologyCreator(log, topology, admin);

        creator.Create();

        Assert.That(TopicAdminObjectExists(qm, "TEST.TOPOCREATOR.A"), Is.True);
        Assert.That(TopicAdminObjectExists(qm, "TEST.TOPOCREATOR.B"), Is.True);
    }

    [Test]
    public void Create_is_idempotent()
    {
        var topology = new TopicTopology { Naming = naming };
        topology.PublishTo(typeof(ConcreteEvent), "TEST.TOPOCREATOR.A", "test/topocreator/a/");

        using var qm = TestBrokerConnection.Connect();
        var admin = new MqAdminConnection(qm, s => s);
        var creator = new TopologyCreator(log, topology, admin);

        creator.Create();
        Assert.DoesNotThrow(() => creator.Create());
    }

    [Test]
    public void Create_skips_routes_without_admin_name()
    {
        var topology = new TopicTopology { Naming = naming };
        topology.PublishTo<ConcreteEvent>("test/topocreator/noadmin/");

        using var qm = TestBrokerConnection.Connect();
        var admin = new MqAdminConnection(qm, s => s);
        var creator = new TopologyCreator(log, topology, admin);

        Assert.DoesNotThrow(() => creator.Create());
        Assert.That(TopicAdminObjectExists(qm, "TEST.TOPOCREATOR.NOADMIN"), Is.False);
    }

    [Test]
    public void Create_does_nothing_when_no_explicit_routes()
    {
        var topology = new TopicTopology { Naming = naming };

        using var qm = TestBrokerConnection.Connect();
        var admin = new MqAdminConnection(qm, s => s);
        var creator = new TopologyCreator(log, topology, admin);

        Assert.DoesNotThrow(() => creator.Create());
    }

    static bool TopicAdminObjectExists(MQQueueManager qm, string topicName)
    {
        var agent = new PCFMessageAgent(qm);
        try
        {
            var request = new PCFMessage(MQC.MQCMD_INQUIRE_TOPIC);
            request.AddParameter(MQC.MQCA_TOPIC_NAME, topicName);
            agent.Send(request);
            return true;
        }
        catch (PCFException)
        {
            return false;
        }
        finally
        {
            agent.Disconnect();
        }
    }

    static void DeleteTopicIfExists(MQQueueManager qm, string topicName)
    {
        var agent = new PCFMessageAgent(qm);
        try
        {
            var command = new PCFMessage(MQC.MQCMD_DELETE_TOPIC);
            command.AddParameter(MQC.MQCA_TOPIC_NAME, topicName);
            agent.Send(command);
        }
        catch (PCFException)
        {
        }
        finally
        {
            agent.Disconnect();
        }
    }
}
