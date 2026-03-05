namespace NServiceBus.Transport.IBMMQ.PerformanceTests.Infrastructure;

using IBM.WMQ;
using IBMMQ;
using Messages;

static class QueueSeeder
{
    public static void Seed(MQQueueManager queueManager, string queueName, int messageCount)
    {
        Seed<PerfTestMessage>(queueManager, queueName, messageCount, "Send");
    }

    public static void Seed<TMessage>(MQQueueManager queueManager, string queueName, int messageCount, string messageIntent = "Send")
    {
        using var queue = queueManager.AccessQueue(queueName, MQC.MQOO_OUTPUT);
        var pmo = new MQPutMessageOptions { Options = MQC.MQPMO_FAIL_IF_QUIESCING };

        for (int i = 0; i < messageCount; i++)
        {
            var messageId = Guid.NewGuid().ToString();
            var headers = new Dictionary<string, string>
            {
                [Headers.MessageId] = messageId,
                [Headers.EnclosedMessageTypes] = typeof(TMessage).FullName!,
                [Headers.ContentType] = "application/json",
                [Headers.NServiceBusVersion] = "10.0.0",
                [Headers.MessageIntent] = messageIntent
            };

            var body = System.Text.Encoding.UTF8.GetBytes($"{{\"Index\":{i}}}");

            var outgoingMessage = new OutgoingMessage(messageId, headers, body);
            var operation = new UnicastTransportOperation(outgoingMessage, queueName, []);

            var mqMessage = IBMMQMessageConverter.ToNative(operation);
            queue.Put(mqMessage, pmo);
        }
    }
}
