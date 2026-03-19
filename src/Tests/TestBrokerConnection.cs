namespace NServiceBus.Transport.IBMMQ.Tests;

using System.Collections;
using IBM.WMQ;

static class TestBrokerConnection
{
    public static readonly Hashtable ConnectionProperties = new()
    {
        { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_MANAGED },
        { MQC.HOST_NAME_PROPERTY, TestConnectionDetails.Host },
        { MQC.PORT_PROPERTY, TestConnectionDetails.Port },
        { MQC.CHANNEL_PROPERTY, TestConnectionDetails.Channel },
        { MQC.USER_ID_PROPERTY, TestConnectionDetails.User },
        { MQC.PASSWORD_PROPERTY, TestConnectionDetails.Password }
    };

    public static MQQueueManager Connect() =>
        new(TestConnectionDetails.QueueManagerName, ConnectionProperties);
}
