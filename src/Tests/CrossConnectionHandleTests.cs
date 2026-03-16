namespace NServiceBus.Transport.IBMMQ.Tests;

using System;
using System.Collections;
using System.Reflection;
using IBM.WMQ;
using NUnit.Framework;

[TestFixture]
public class CrossConnectionHandleTests
{
    static readonly Hashtable ConnectionProperties = new()
    {
        { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_MANAGED },
        { MQC.HOST_NAME_PROPERTY, TestConnectionDetails.Host },
        { MQC.PORT_PROPERTY, TestConnectionDetails.Port },
        { MQC.CHANNEL_PROPERTY, TestConnectionDetails.Channel },
        { MQC.USER_ID_PROPERTY, TestConnectionDetails.User },
        { MQC.PASSWORD_PROPERTY, TestConnectionDetails.Password }
    };

    static readonly FieldInfo QMgrField = typeof(MQDestination)
        .GetField("qMgr", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [Test]
    public void Put_via_handle_on_wrong_connection_fails_with_HOBJ_ERROR()
    {
        Assert.That(QMgrField, Is.Not.Null, "Cannot find qMgr field on MQDestination hierarchy");

        var connA = new MQQueueManager(TestConnectionDetails.QueueManagerName, ConnectionProperties);
        var connB = new MQQueueManager(TestConnectionDetails.QueueManagerName, ConnectionProperties);
        var queue = connA.AccessQueue("SYSTEM.DEFAULT.LOCAL.QUEUE", MQC.MQOO_OUTPUT);
        var originalQMgr = QMgrField.GetValue(queue);
        QMgrField.SetValue(queue, connB);

        try
        {
            var ex = Assert.Throws<MQException>(() => queue.Put(new MQMessage()));
            Assert.That(ex!.ReasonCode, Is.EqualTo(MQC.MQRC_HOBJ_ERROR));
        }
        finally
        {
            QMgrField.SetValue(queue, originalQMgr);
            queue.Close();
            connA.Disconnect();
            connB.Disconnect();
        }
    }
}
