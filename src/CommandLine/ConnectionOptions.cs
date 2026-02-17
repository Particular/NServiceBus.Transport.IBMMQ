namespace NServiceBus.Transport.IbmMq.CommandLine;

using System.Collections;
using IBM.WMQ;
using McMaster.Extensions.CommandLineUtils;

class ConnectionOptions
{
    ConnectionOptions(
        CommandOption host,
        CommandOption port,
        CommandOption channel,
        CommandOption queueManager,
        CommandOption user,
        CommandOption password)
    {
        this.host = host;
        this.port = port;
        this.channel = channel;
        this.queueManager = queueManager;
        this.user = user;
        this.password = password;
    }

    readonly CommandOption host;
    readonly CommandOption port;
    readonly CommandOption channel;
    readonly CommandOption queueManager;
    readonly CommandOption user;
    readonly CommandOption password;

    public static ConnectionOptions Register(CommandLineApplication app) => new(
        app.Option("--host", "IBM MQ host (env: IBMMQ_HOST, default: localhost)", CommandOptionType.SingleValue),
        app.Option("--port", "IBM MQ port (env: IBMMQ_PORT, default: 1414)", CommandOptionType.SingleValue),
        app.Option("--channel", "IBM MQ channel (env: IBMMQ_CHANNEL, default: DEV.ADMIN.SVRCONN)", CommandOptionType.SingleValue),
        app.Option("--queue-manager", "Queue manager name (env: IBMMQ_QUEUE_MANAGER)", CommandOptionType.SingleValue),
        app.Option("--user", "User ID (env: IBMMQ_USER)", CommandOptionType.SingleValue),
        app.Option("--password", "Password (env: IBMMQ_PASSWORD)", CommandOptionType.SingleValue)
    );

    public MQQueueManager Connect()
    {
        var hostValue = Resolve(host, "IBMMQ_HOST", "localhost");
        var portValue = int.Parse(Resolve(port, "IBMMQ_PORT", "1414"));
        var channelValue = Resolve(channel, "IBMMQ_CHANNEL", "DEV.ADMIN.SVRCONN");
        var qmValue = Resolve(queueManager, "IBMMQ_QUEUE_MANAGER", "");
        var userValue = Resolve(user, "IBMMQ_USER");
        var passwordValue = Resolve(password, "IBMMQ_PASSWORD");

        var properties = new Hashtable
        {
            [MQC.TRANSPORT_PROPERTY] = MQC.TRANSPORT_MQSERIES_MANAGED,
            [MQC.HOST_NAME_PROPERTY] = hostValue,
            [MQC.PORT_PROPERTY] = portValue,
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

        return new MQQueueManager(qmValue, properties);
    }

    static string Resolve(CommandOption option, string envVar, string defaultValue = "") =>
        option.Value() ?? Environment.GetEnvironmentVariable(envVar) ?? defaultValue;
}
