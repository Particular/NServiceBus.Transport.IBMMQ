namespace NServiceBus.Transport.IBMMQ.Tests;

using System.Reflection;
using IBM.WMQ;
using NUnit.Framework;

/// <summary>
/// Proves that IBM MQ handles are bound to the connection that created them.
/// Uses reflection to access MQDestination.qMgr — an internal field in the IBM.WMQ
/// managed client. If the IBM client library is updated, verify this field still exists
/// and behaves the same way.
/// </summary>
[TestFixture]
[Category("RequiresBroker")]
public class CrossConnectionHandleTests
{
    static readonly FieldInfo QMgrField = typeof(MQDestination)
        .GetField("qMgr", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [OneTimeSetUp]
    public void Setup() => BrokerRequirement.Verify();

    [Test]
    public void Put_via_handle_on_wrong_connection_fails_with_HOBJ_ERROR()
    {
        Assert.That(QMgrField, Is.Not.Null, "Cannot find qMgr field on MQDestination hierarchy");

        var connA = TestBrokerConnection.Connect();
        var connB = TestBrokerConnection.Connect();
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
