namespace NServiceBus.Transport.IbmMq.CommandLine;

using System.Collections;
using IBM.WMQ;
using McMaster.Extensions.CommandLineUtils;

class CommandRunner : IDisposable
{
    CommandRunner(MQQueueManager queueManager) => QueueManager = queueManager;

    public MQQueueManager QueueManager { get; }

    public static CommandRunner Create(
        CommandOption host,
        CommandOption port,
        CommandOption channel,
        CommandOption queueManager,
        CommandOption user,
        CommandOption password)
    {
        var hostValue = host.Value() ?? Environment.GetEnvironmentVariable("IBMMQ_HOST") ?? "localhost";
        var portValue = port.Value() ?? Environment.GetEnvironmentVariable("IBMMQ_PORT") ?? "1414";
        var channelValue = channel.Value() ?? Environment.GetEnvironmentVariable("IBMMQ_CHANNEL") ?? "DEV.ADMIN.SVRCONN";
        var qmValue = queueManager.Value() ?? Environment.GetEnvironmentVariable("IBMMQ_QUEUE_MANAGER") ?? "";
        var userValue = user.Value() ?? Environment.GetEnvironmentVariable("IBMMQ_USER");
        var passwordValue = password.Value() ?? Environment.GetEnvironmentVariable("IBMMQ_PASSWORD");

        var properties = new Hashtable
        {
            [MQC.TRANSPORT_PROPERTY] = MQC.TRANSPORT_MQSERIES_MANAGED,
            [MQC.HOST_NAME_PROPERTY] = hostValue,
            [MQC.PORT_PROPERTY] = int.Parse(portValue),
            [MQC.CHANNEL_PROPERTY] = channelValue,
            [MQC.CONNECT_OPTIONS_PROPERTY] = MQC.MQCNO_RECONNECT_DISABLED,
            [MQC.APPNAME_PROPERTY] = "ibmmq-transport"
        };

        if (!string.IsNullOrWhiteSpace(userValue))
        {
            properties[MQC.USE_MQCSP_AUTHENTICATION_PROPERTY] = true;
            properties[MQC.USER_ID_PROPERTY] = userValue;
        }

        if (!string.IsNullOrWhiteSpace(passwordValue))
        {
            properties[MQC.PASSWORD_PROPERTY] = passwordValue;
        }

        var connection = new MQQueueManager(qmValue, properties);
        return new CommandRunner(connection);
    }

    public void Dispose() => QueueManager.Disconnect();
}
