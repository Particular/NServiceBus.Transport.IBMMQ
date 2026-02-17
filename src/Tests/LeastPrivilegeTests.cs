namespace NServiceBus.Transport.IbmMq.Tests;

using System;
using System.Collections;
using IBM.WMQ;
using IBM.WMQ.PCF;
using NUnit.Framework;

/// <summary>
/// Integration tests verifying admin vs app permission separation.
///
/// Admin credentials (EnableInstallers phase):
///   - Can create/delete queues, topics, subscriptions via PCF
///   - Used during first run with EnableInstallers to set up infrastructure
///
/// App credentials (runtime phase):
///   - Can send/receive messages, publish/subscribe to topics
///   - Cannot create/delete MQ objects via PCF
///
/// Run extra/setup-leastpriv-tests.sh before executing these tests.
/// </summary>
[TestFixture]
[Category("Integration")]
public class LeastPrivilegeTests
{
    static string QueueManagerName => TestConnectionDetails.QueueManagerName;
    static string AdminChannel => TestConnectionDetails.Channel;
    const string AppChannel = "DEV.APP.SVRCONN";
    const string AppUser = "testapp";
    const string AppPassword = "testpass1";
    const string SendQueue = "TEST.LEASTPRIV.SEND";
    const string ReceiveQueue = "TEST.LEASTPRIV.RECEIVE";
    const string TopicName = "TEST.LEASTPRIV.TOPIC";
    const string TopicString = "test/leastpriv/topic";

    static string AdminHost => TestConnectionDetails.Host;
    static string AdminUser => TestConnectionDetails.User;
    static string AdminPassword => TestConnectionDetails.Password;
    static int AdminPort => TestConnectionDetails.Port;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        try
        {
            using var qm = CreateAppConnection();
            qm.Disconnect();
        }
        catch (MQException)
        {
            Assert.Ignore(
                "Cannot connect as testapp. Run extra/setup-leastpriv-tests.sh to configure the test environment.");
        }
    }

    // ---- Admin role: installer phase (create infrastructure) ----

    [Test]
    public void Admin_can_create_queue()
    {
        const string queueName = "TEST.LEASTPRIV.ADMINCREATED";
        using var qm = CreateAdminConnection();
        var agent = new PCFMessageAgent(qm);
        try
        {
            var command = new PCFMessage(MQC.MQCMD_CREATE_Q);
            command.AddParameter(MQC.MQCA_Q_NAME, queueName);
            command.AddParameter(MQC.MQIA_Q_TYPE, MQC.MQQT_LOCAL);
            agent.Send(command);
        }
        catch (PCFException e) when (e.ReasonCode == MQC.MQRCCF_OBJECT_ALREADY_EXISTS)
        {
            // Already exists from a previous run, that's fine
        }
        finally
        {
            // Clean up
            try
            {
                var delete = new PCFMessage(MQC.MQCMD_DELETE_Q);
                delete.AddParameter(MQC.MQCA_Q_NAME, queueName);
                agent.Send(delete);
            }
            catch
            {
                // Best-effort cleanup
            }
            agent.Disconnect();
        }
        qm.Disconnect();
    }

    [Test]
    public void Admin_can_create_topic()
    {
        const string topicName = "TEST.LEASTPRIV.ADMINTOPIC";
        using var qm = CreateAdminConnection();
        var agent = new PCFMessageAgent(qm);
        try
        {
            var command = new PCFMessage(MQC.MQCMD_CREATE_TOPIC);
            command.AddParameter(MQC.MQCA_TOPIC_NAME, topicName);
            command.AddParameter(MQC.MQCA_TOPIC_STRING, "test/leastpriv/admintopic");
            agent.Send(command);
        }
        catch (PCFException e) when (e.ReasonCode == MQC.MQRCCF_OBJECT_ALREADY_EXISTS)
        {
            // Already exists from a previous run
        }
        finally
        {
            try
            {
                var delete = new PCFMessage(MQC.MQCMD_DELETE_TOPIC);
                delete.AddParameter(MQC.MQCA_TOPIC_NAME, topicName);
                agent.Send(delete);
            }
            catch
            {
                // Best-effort cleanup
            }
            agent.Disconnect();
        }
        qm.Disconnect();
    }

    // ---- App role: runtime phase (send/receive/pub/sub) ----

    [Test]
    public void App_can_send_to_queue()
    {
        using var qm = CreateAppConnection();
        using var queue = qm.AccessQueue(SendQueue, MQC.MQOO_OUTPUT);

        var msg = new MQMessage();
        msg.WriteString("app send test");
        queue.Put(msg);

        queue.Close();
        qm.Disconnect();
    }

    [Test]
    public void App_can_receive_from_queue()
    {
        // Seed a message as admin
        using (var adminQm = CreateAdminConnection())
        {
            using var queue = adminQm.AccessQueue(ReceiveQueue, MQC.MQOO_OUTPUT);
            var msg = new MQMessage();
            msg.WriteString("app receive test");
            queue.Put(msg);
            queue.Close();
            adminQm.Disconnect();
        }

        // Receive as app user
        using var qm = CreateAppConnection();
        using var recvQueue = qm.AccessQueue(ReceiveQueue, MQC.MQOO_INPUT_AS_Q_DEF);
        var gmo = new MQGetMessageOptions { Options = MQC.MQGMO_NO_WAIT };
        var received = new MQMessage();
        recvQueue.Get(received, gmo);

        Assert.That(received.ReadString(received.DataLength), Does.Contain("app receive test"));

        recvQueue.Close();
        qm.Disconnect();
    }

    [Test]
    public void App_can_publish_to_topic()
    {
        using var qm = CreateAppConnection();
        using var topic = qm.AccessTopic(
            null,
            TopicName,
            MQC.MQTOPIC_OPEN_AS_PUBLICATION,
            MQC.MQOO_OUTPUT);

        var msg = new MQMessage();
        msg.WriteString("app publish test");
        topic.Put(msg);

        topic.Close();
        qm.Disconnect();
    }

    [Test]
    public void App_can_subscribe_to_topic()
    {
        var subscriptionName = "TEST.LEASTPRIV.APPSUB";

        using var qm = CreateAppConnection();
        using var destQueue = qm.AccessQueue(ReceiveQueue, MQC.MQOO_OUTPUT);
        using var topic = qm.AccessTopic(
            destQueue,
            TopicString,
            null,
            MQC.MQSO_CREATE | MQC.MQSO_DURABLE | MQC.MQSO_FAIL_IF_QUIESCING,
            null,
            subscriptionName);

        topic.Close();
        destQueue.Close();
        qm.Disconnect();

        // Clean up subscription via admin
        DeleteSubscriptionAsAdmin(subscriptionName);
    }

    // ---- App role: cannot create infrastructure ----

    [Test]
    public void App_cannot_create_queue()
    {
        using var qm = CreateAppConnection();

        var ex = Assert.Throws<MQException>(() =>
        {
            var agent = new PCFMessageAgent(qm);
            try
            {
                var command = new PCFMessage(MQC.MQCMD_CREATE_Q);
                command.AddParameter(MQC.MQCA_Q_NAME, "TEST.UNAUTHORIZED.QUEUE");
                command.AddParameter(MQC.MQIA_Q_TYPE, MQC.MQQT_LOCAL);
                agent.Send(command);
            }
            finally
            {
                agent.Disconnect();
            }
        });

        Assert.That(ex!.ReasonCode, Is.EqualTo(MQC.MQRC_NOT_AUTHORIZED));
        qm.Disconnect();
    }

    [Test]
    public void App_cannot_create_topic()
    {
        using var qm = CreateAppConnection();

        var ex = Assert.Throws<PCFException>(() =>
        {
            var agent = new PCFMessageAgent(qm);
            try
            {
                var command = new PCFMessage(MQC.MQCMD_CREATE_TOPIC);
                command.AddParameter(MQC.MQCA_TOPIC_NAME, "TEST.UNAUTHORIZED.TOPIC");
                command.AddParameter(MQC.MQCA_TOPIC_STRING, "test/unauthorized/topic");
                agent.Send(command);
            }
            finally
            {
                agent.Disconnect();
            }
        });

        Assert.That(ex!.ReasonCode, Is.EqualTo(MQC.MQRC_NOT_AUTHORIZED));
        qm.Disconnect();
    }

    [Test]
    public void App_cannot_delete_subscription()
    {
        // First create a subscription as admin
        var subscriptionName = "TEST.LEASTPRIV.NODELETE";

        using (var adminQm = CreateAdminConnection())
        {
            using var destQueue = adminQm.AccessQueue(ReceiveQueue, MQC.MQOO_OUTPUT);
            using var topic = adminQm.AccessTopic(
                destQueue,
                TopicString,
                null,
                MQC.MQSO_CREATE | MQC.MQSO_DURABLE | MQC.MQSO_FAIL_IF_QUIESCING,
                null,
                subscriptionName);
            topic.Close();
            destQueue.Close();
            adminQm.Disconnect();
        }

        // Try to delete it as app user via PCF
        using var qm = CreateAppConnection();

        var ex = Assert.Throws<MQException>(() =>
        {
            var agent = new PCFMessageAgent(qm);
            try
            {
                var command = new PCFMessage(MQC.MQCMD_DELETE_SUBSCRIPTION);
                command.AddParameter(MQC.MQCACF_SUB_NAME, subscriptionName);
                agent.Send(command);
            }
            finally
            {
                agent.Disconnect();
            }
        });

        Assert.That(ex!.ReasonCode, Is.EqualTo(MQC.MQRC_NOT_AUTHORIZED));
        qm.Disconnect();

        // Clean up via admin
        DeleteSubscriptionAsAdmin(subscriptionName);
    }

    // ---- Helpers ----

    static MQQueueManager CreateAdminConnection()
    {
        var properties = new Hashtable
        {
            { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_MANAGED },
            { MQC.HOST_NAME_PROPERTY, AdminHost },
            { MQC.PORT_PROPERTY, AdminPort },
            { MQC.CHANNEL_PROPERTY, AdminChannel },
            { MQC.USER_ID_PROPERTY, AdminUser },
            { MQC.PASSWORD_PROPERTY, AdminPassword }
        };
        return new MQQueueManager(QueueManagerName, properties);
    }

    static MQQueueManager CreateAppConnection()
    {
        var properties = new Hashtable
        {
            { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_MANAGED },
            { MQC.HOST_NAME_PROPERTY, AdminHost },
            { MQC.PORT_PROPERTY, AdminPort },
            { MQC.CHANNEL_PROPERTY, AppChannel },
            { MQC.USER_ID_PROPERTY, AppUser },
            { MQC.PASSWORD_PROPERTY, AppPassword }
        };
        return new MQQueueManager(QueueManagerName, properties);
    }

    static void DeleteSubscriptionAsAdmin(string subscriptionName)
    {
        try
        {
            using var adminQm = CreateAdminConnection();
            var agent = new PCFMessageAgent(adminQm);
            try
            {
                var command = new PCFMessage(MQC.MQCMD_DELETE_SUBSCRIPTION);
                command.AddParameter(MQC.MQCACF_SUB_NAME, subscriptionName);
                agent.Send(command);
            }
            catch (PCFException)
            {
                // Subscription may not exist
            }
            finally
            {
                agent.Disconnect();
            }
            adminQm.Disconnect();
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
