namespace NServiceBus.Transport.IBMMQ;

using System.Diagnostics;
using System.Reflection;

static class ActivitySources
{
    public const string Name = "NServiceBus.Transport.IBMMQ";

    // Activity names
    public const string Receive = "NServiceBus.Transport.IBMMQ.Receive";
    public const string Dispatch = "NServiceBus.Transport.IBMMQ.Dispatch";
    public const string PutToQueue = "NServiceBus.Transport.IBMMQ.PutToQueue";
    public const string PutToTopic = "NServiceBus.Transport.IBMMQ.PutToTopic";
    public const string Attempt = "NServiceBus.Transport.IBMMQ.Attempt";

    // OTel messaging semantic convention tags
    public const string TagMessagingSystem = "messaging.system";
    public const string TagMessagingSystemValue = "ibm_mq";
    public const string TagDestinationName = "messaging.destination.name";
    public const string TagOperationType = "messaging.operation.type";
    public const string TagMessageId = "messaging.message.id";
    public const string TagBatchMessageCount = "messaging.batch.message_count";

    // OTel messaging operation types
    public const string OperationSend = "send";
    public const string OperationPublish = "publish";
    public const string OperationReceive = "receive";

    // Vendor-specific tags
    public const string TagTopicString = "nservicebus.transport.ibmmq.topic_string";
    public const string TagFailureCount = "nservicebus.transport.ibmmq.failure_count";

    // Activity event names
    public const string CommitEvent = "mq.commit";
    public const string BackoutEvent = "mq.backout";

    static readonly string Version = GetVersion();

    static string GetVersion()
    {
        var informationalVersion = typeof(ActivitySources).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plusIndex = informationalVersion.IndexOf('+');
        return plusIndex >= 0 ? informationalVersion[..plusIndex] : informationalVersion;
    }

    public static readonly ActivitySource Main = new(Name, Version);
}
