namespace NServiceBus.Transport.IBMMQ;

/// <summary>
/// Creates a MqAdminConnection for admin/subscription operations
/// </summary>
delegate MqAdminConnection CreateMqAdminConnection();

/// <summary>
/// Sanitizer topic and queue resource names
/// </summary>
public delegate string SanitizeResourceName(string queueName);
