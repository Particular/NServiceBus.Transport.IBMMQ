using System;
using System.Text;
using IBM.WMQ;

Console.WriteLine("🚀 Starting IBM MQ Publisher...");

// Register encoding support (Not required for UTF-8, but good practice)
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

try
{
    // Set MQ connection properties
    MQEnvironment.Hostname = "localhost";
    MQEnvironment.Channel = "DEV.ADMIN.SVRCONN";
    MQEnvironment.Port = 1414;
    MQEnvironment.UserId = "admin";
    MQEnvironment.Password = "passw0rd";

    // Connect to the queue manager
    using MQQueueManager queueManager = new MQQueueManager("QM1");
    Console.WriteLine("✅ Connected to queue manager QM1");

    // Open the topic for publishing
    using MQTopic topic = queueManager.AccessTopic(
        "DEV.BASE.TOPIC",
        null,
        MQC.MQTOPIC_OPEN_AS_PUBLICATION,
        MQC.MQOO_OUTPUT);

    Console.WriteLine("✅ Opened topic 'DEV.BASE.TOPIC' for publishing");

    // Main loop: Listen for user input
    while (true)
    {
        Console.WriteLine("\n📢 Press 'P' to publish a message, 'Q' to quit.");
        var key = Console.ReadKey(true).Key;

        if (key == ConsoleKey.Q)
        {
            Console.WriteLine("👋 Exiting publisher...");
            break;
        }
        else if (key == ConsoleKey.P)
        {
            string messageId = Guid.NewGuid().ToString().Substring(0, 6);
            string message = $"Hello from IBM MQ Publisher! Message ID: {messageId}";
            PublishMessage(topic, message);
        }
    }
}
catch (MQException ex)
{
    Console.WriteLine($"❌ MQException: {ex.ReasonCode} - {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Unexpected error: {ex.Message}");
}

void PublishMessage(MQTopic topic, string message)
{
    try
    {
        // Convert message to bytes
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);

        // Create an MQ message
        MQMessage mqMessage = new MQMessage();
        mqMessage.Write(messageBytes);
        mqMessage.Format = MQC.MQFMT_STRING;

        // Put the message on the topic
        topic.Put(mqMessage);

        Console.WriteLine($"📢 Published message: {message}");
    }
    catch (MQException ex)
    {
        Console.WriteLine($"❌ Failed to publish message. MQException: {ex.ReasonCode} - {ex.Message}");
    }
}
