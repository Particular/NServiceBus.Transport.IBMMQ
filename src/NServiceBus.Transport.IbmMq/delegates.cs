namespace NServiceBus.Transport.IbmMq;

using IBM.WMQ;

/// <summary>
/// Creates a new queue manager connection
/// </summary>
delegate MQQueueManager CreateQueueManager();

/// <summary>
/// Creates a MqQueueManagerFacade for a given queue manager
/// </summary>
delegate MqQueueManagerFacade CreateQueueManagerFacade(MQQueueManager queueManager);

/// <summary>
/// Sanitizer topic and queue resource names
/// </summary>
public delegate string SanitizeResourceName(string queueName);