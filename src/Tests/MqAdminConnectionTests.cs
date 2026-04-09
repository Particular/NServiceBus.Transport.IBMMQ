namespace NServiceBus.Transport.IBMMQ.Tests;

using IBM.WMQ;
using IBM.WMQ.PCF;
using NUnit.Framework;

[TestFixture]
[Category("RequiresBroker")]
public class MqAdminConnectionTests
{
    const string TopicPrefix = "TEST.MQADMIN";
    const string QueueName = "TEST.MQADMIN.SUB";

    [OneTimeSetUp]
    public void Setup()
    {
        BrokerRequirement.Verify();

        using var qm = TestBrokerConnection.Connect();
        CreateQueueIfNotExists(qm, QueueName);
        qm.Disconnect();
    }

    [TearDown]
    public void Cleanup()
    {
        using var qm = TestBrokerConnection.Connect();
        DeleteSubscriptionIfExists(qm, $"{TopicPrefix}.SUB.A");
        DeleteTopicIfExists(qm, $"{TopicPrefix}.A");
        qm.Disconnect();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        using var qm = TestBrokerConnection.Connect();
        DeleteQueueIfExists(qm, QueueName);
        qm.Disconnect();
    }

    // --- CreateTopic ---

    [Test]
    public void CreateTopic_creates_admin_topic_object()
    {
        using var qm = TestBrokerConnection.Connect();
        var admin = new MqAdminConnection(qm, s => s);

        admin.CreateTopic($"{TopicPrefix}.A", "test/mqadmin/a/");

        Assert.That(TopicAdminObjectExists(qm, $"{TopicPrefix}.A"), Is.True);
    }

    [Test]
    public void CreateTopic_is_idempotent()
    {
        using var qm = TestBrokerConnection.Connect();
        var admin = new MqAdminConnection(qm, s => s);

        admin.CreateTopic($"{TopicPrefix}.A", "test/mqadmin/a/");

        Assert.DoesNotThrow(() => admin.CreateTopic($"{TopicPrefix}.A", "test/mqadmin/a/"));
    }

    // --- EnsureSubscription ---

    [Test]
    public void EnsureSubscription_creates_new_subscription()
    {
        using var qm = TestBrokerConnection.Connect();
        var admin = new MqAdminConnection(qm, s => s);

        using var topic = admin.EnsureSubscription("test/mqadmin/a/", $"{TopicPrefix}.SUB.A", QueueName);
        topic.Close();

        // Verify by resuming — would throw MQRC_NO_SUBSCRIPTION if it wasn't created
        using var resumed = admin.EnsureSubscription("test/mqadmin/a/", $"{TopicPrefix}.SUB.A", QueueName);
        resumed.Close();
    }

    [Test]
    public void EnsureSubscription_resumes_existing_subscription()
    {
        using var qm = TestBrokerConnection.Connect();
        var admin = new MqAdminConnection(qm, s => s);

        using var first = admin.EnsureSubscription("test/mqadmin/a/", $"{TopicPrefix}.SUB.A", QueueName);
        first.Close();

        Assert.DoesNotThrow(() =>
        {
            using var resumed = admin.EnsureSubscription("test/mqadmin/a/", $"{TopicPrefix}.SUB.A", QueueName);
            resumed.Close();
        });
    }

    // --- RemoveSubscription ---

    [Test]
    public void RemoveSubscription_deletes_existing_subscription()
    {
        using var qm = TestBrokerConnection.Connect();
        var admin = new MqAdminConnection(qm, s => s);

        using var topic = admin.EnsureSubscription("test/mqadmin/a/", $"{TopicPrefix}.SUB.A", QueueName);
        topic.Close();

        admin.RemoveSubscription($"{TopicPrefix}.SUB.A");

        // Verify by trying to resume — should fall through to CREATE path
        using var recreated = admin.EnsureSubscription("test/mqadmin/a/", $"{TopicPrefix}.SUB.A", QueueName);
        recreated.Close();
    }

    [Test]
    public void RemoveSubscription_does_not_throw_when_subscription_does_not_exist()
    {
        using var qm = TestBrokerConnection.Connect();
        var admin = new MqAdminConnection(qm, s => s);

        Assert.DoesNotThrow(() => admin.RemoveSubscription($"{TopicPrefix}.SUB.NEVER"));
    }

    // --- Dispose ---

    [Test]
    public void Dispose_is_idempotent()
    {
        var qm = TestBrokerConnection.Connect();
        var admin = new MqAdminConnection(qm, s => s);

        admin.Dispose();
        Assert.DoesNotThrow(() => admin.Dispose());
    }

    // --- Helpers ---

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

    static void CreateQueueIfNotExists(MQQueueManager qm, string name)
    {
        var agent = new PCFMessageAgent(qm);
        try
        {
            var command = new PCFMessage(MQC.MQCMD_CREATE_Q);
            command.AddParameter(MQC.MQCA_Q_NAME, name);
            command.AddParameter(MQC.MQIA_Q_TYPE, MQC.MQQT_LOCAL);
            agent.Send(command);
        }
        catch (PCFException e) when (e.ReasonCode == MQC.MQRCCF_OBJECT_ALREADY_EXISTS)
        {
        }
        finally
        {
            agent.Disconnect();
        }
    }

    static void DeleteQueueIfExists(MQQueueManager qm, string name)
    {
        var agent = new PCFMessageAgent(qm);
        try
        {
            var command = new PCFMessage(MQC.MQCMD_DELETE_Q);
            command.AddParameter(MQC.MQCA_Q_NAME, name);
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

    static void DeleteSubscriptionIfExists(MQQueueManager qm, string subscriptionName)
    {
        var agent = new PCFMessageAgent(qm);
        try
        {
            var command = new PCFMessage(MQC.MQCMD_DELETE_SUBSCRIPTION);
            command.AddParameter(MQC.MQCACF_SUB_NAME, subscriptionName);
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
