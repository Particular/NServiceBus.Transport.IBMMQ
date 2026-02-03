using IBM.WMQ;
using System.Text;

// Register legacy encodings (Required for IBM MQ)
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

Console.WriteLine("🚀 Starting IBM MQ Durable Subscriber...");

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

    // Open a durable subscription
    using MQTopic subscriber = ResumeOrCreateTopicSubscription(queueManager, "DEV.BASE.TOPIC", "my.sub");

    Console.WriteLine("✅ Subscribed to DEV.BASE.TOPIC. Waiting for messages...");

    // Create MQ Get Message Options
    var gmo = new MQGetMessageOptions
    {
        Options = MQC.MQGMO_SYNCPOINT |       // Process messages in a transaction (commit/backout)
                  MQC.MQGMO_FAIL_IF_QUIESCING // Fail if the queue manager is quiescing (shutting down)
    };

    // Exponential backoff settings
    int backoffTime = 1000;
    const int maxBackoff = 30000;
    Random random = new Random();

    while (true)
    {
        Console.WriteLine("\n🔄 Listening... Press 'Q' to quit.");
        if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
        {
            Console.WriteLine("👋 Exiting subscriber...");
            break;
        }

        bool messagesReceived = false;

        while (true)
        {
            try
            {
                MQMessage receivedMessage = new MQMessage();
                subscriber.Get(receivedMessage, gmo);

                string messageText = receivedMessage.ReadString(receivedMessage.MessageLength);
                Console.WriteLine($"📩 Received: {messageText}");

                // Simulate message processing (20% chance of failing)
                if (ProcessMessage(messageText, random))
                {
                    // Commit the transaction
                    queueManager.Commit();
                    Console.WriteLine("✅ Message processed successfully.");
                } else
                {
                    throw new Exception("Simulated processing failure.");
                }

                messagesReceived = true;
            }
            catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
            {
                break; // No messages available
            }
            catch (MQException ex)
            {
                Console.WriteLine($"❌ MQException: {ex.ReasonCode} - {ex.Message}");
                return;
            }
            catch (Exception ex)
            {
                // Rollback the transaction
                queueManager.Backout();
                Console.WriteLine("❌ Message processing failed. Transaction rolled back.");
            }
        }

        ApplyExponentialBackoff(ref backoffTime, maxBackoff, messagesReceived, random);
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

static MQTopic ResumeOrCreateTopicSubscription(MQQueueManager queueManager, string topicName, string subscriptionName)
{
    try
    {
        Console.WriteLine($"🔄 Attempting to resume durable subscription '{subscriptionName}'...");
        return AccessTopic(queueManager, topicName, subscriptionName, MQC.MQSO_RESUME);
    }
    catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_SUBSCRIPTION)
    {
        Console.WriteLine($"⚠️ Subscription '{subscriptionName}' not found. Creating a new one...");
        return AccessTopic(queueManager, topicName, subscriptionName, MQC.MQSO_CREATE);
    }
    catch (MQException ex)
    {
        Console.WriteLine($"❌ Failed to access subscription '{subscriptionName}'. Error: {ex.ReasonCode} - {ex.Message}");
        throw;
    }
}

static MQTopic AccessTopic(MQQueueManager queueManager, string topicName, string subscriptionName, int options)
{
    return queueManager.AccessTopic(
        topicName,
        null,
        options |
        MQC.MQSO_FAIL_IF_QUIESCING | // Fail if the queue manager is quiescing (shutting down)
        MQC.MQSO_DURABLE, // Durable subscription (survives restarts)
        null,
        subscriptionName);
}

static void ApplyExponentialBackoff(ref int backoffTime, int maxBackoff, bool messagesReceived, Random random)
{
    if (messagesReceived)
    {
        backoffTime = 1000; // Reset backoff time
    }
    else
    {
        // Apply Jitter: Randomly adjust backoff between 80% - 120%
        double jitterFactor = random.NextDouble() * (1.2 - 0.8) + 0.8;
        int adjustedBackoff = (int)(backoffTime * jitterFactor);

        Console.WriteLine($"⚠️ No messages available, backing off for {adjustedBackoff / 1000.0:F1} sec...");
        Thread.Sleep(adjustedBackoff);

        // Increase backoff time (exponential growth up to maxBackoff)
        backoffTime = Math.Min(backoffTime * 2, maxBackoff);
    }
}

// Simulated message processing (20% chance of failure)
static bool ProcessMessage(string message, Random random)
{
    bool success = random.NextDouble() >= 0.2; // 80% success, 20% failure
    if (!success)
    {
        Console.WriteLine("⚠️ Simulated failure: Message processing failed.");
    }

    return success;
}
