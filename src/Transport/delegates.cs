namespace NServiceBus.Transport.IBMMQ;

/// <summary>
/// Creates a MqAdminConnection for admin/subscription operations
/// </summary>
delegate MqAdminConnection CreateMqAdminConnection();

/// <summary>
/// Creates a topic on the queue manager if it does not already exist
/// </summary>
delegate void CreateTopic(string topicName, string topicString);

/// <summary>
/// Creates a ReceiveStrategy for processing messages with the given runtime context
/// </summary>
delegate ReceiveStrategy CreateReceiveStrategy(ReceiveContext context);

/// <summary>
/// Creates a MessagePumpWorker with the given runtime context
/// </summary>
delegate MessagePumpWorker CreateMessagePumpWorker(string queueName, OnMessage onMessage, OnError onError, int workerIndex);

/// <summary>
/// Sanitizer topic and queue resource names
/// </summary>
public delegate string SanitizeResourceName(string queueName);
