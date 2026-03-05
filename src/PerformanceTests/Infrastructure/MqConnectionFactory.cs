namespace NServiceBus.Transport.IBMMQ.PerformanceTests.Infrastructure;

using System.Collections;
using System.IO.Hashing;
using System.Text;
using IBM.WMQ;

static class MqConnectionFactory
{
    public static Hashtable BuildConnectionProperties()
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

    public static MQQueueManager CreateQueueManager()
    {
        return new MQQueueManager(TestConnectionDetails.QueueManagerName, BuildConnectionProperties());
    }

    public static string FormatQueueName(string name)
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
}
