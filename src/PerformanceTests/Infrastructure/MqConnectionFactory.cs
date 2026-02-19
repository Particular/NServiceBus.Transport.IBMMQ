namespace NServiceBus.Transport.IbmMq.PerformanceTests.Infrastructure;

using System.Collections;
using System.IO.Hashing;
using System.Text;
using IBM.WMQ;

static class MqConnectionFactory
{
    static readonly string ConnectionDetails =
        Environment.GetEnvironmentVariable("IbmMq_ConnectionDetails") ?? "localhost;admin;passw0rd";

    public static (string Host, string User, string Password, int Port) ParseConnectionDetails()
    {
        var parts = ConnectionDetails.Split(';');
        var host = parts.Length > 0 ? parts[0] : "localhost";
        var user = parts.Length > 1 ? parts[1] : "admin";
        var password = parts.Length > 2 ? parts[2] : "passw0rd";
        var port = parts.Length > 3 && int.TryParse(parts[3], out var p) ? p : 1414;
        return (host, user, password, port);
    }

    public static Hashtable BuildConnectionProperties()
    {
        var (host, user, password, port) = ParseConnectionDetails();

        return new Hashtable
        {
            { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_MANAGED },
            { MQC.HOST_NAME_PROPERTY, host },
            { MQC.PORT_PROPERTY, port },
            { MQC.CHANNEL_PROPERTY, "DEV.ADMIN.SVRCONN" },
            { MQC.USER_ID_PROPERTY, user },
            { MQC.PASSWORD_PROPERTY, password }
        };
    }

    public static MQQueueManager CreateQueueManager()
    {
        return new MQQueueManager("QM1", BuildConnectionProperties());
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
