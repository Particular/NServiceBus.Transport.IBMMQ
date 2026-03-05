namespace NServiceBus.Transport.IBMMQ.CommandLine;

using IBM.WMQ;
using IBM.WMQ.PCF;

static class Subscription
{
    public static void Create(MQQueueManager queueManager, string topicString, string subscriptionName, string endpointQueue)
    {
        try
        {
            Resume(queueManager, topicString, subscriptionName);
            Console.WriteLine($"Subscription '{subscriptionName}' already exists, skipping creation.");
        }
        catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_SUBSCRIPTION)
        {
            CreateDurable(queueManager, topicString, subscriptionName, endpointQueue);
            Console.WriteLine($"Subscription '{subscriptionName}' created successfully.");
        }
    }

    public static void Delete(MQQueueManager queueManager, string subscriptionName)
    {
        var agent = new PCFMessageAgent(queueManager);
        try
        {
            var command = new PCFMessage(MQC.MQCMD_DELETE_SUBSCRIPTION);
            command.AddParameter(MQC.MQCACF_SUB_NAME, subscriptionName);
            agent.Send(command);

            Console.WriteLine($"Subscription '{subscriptionName}' deleted successfully.");
        }
        catch (PCFException e) when (e.ReasonCode == MQC.MQRCCF_SUB_NAME_ERROR)
        {
            Console.WriteLine($"Subscription '{subscriptionName}' does not exist, skipping deletion.");
        }
        finally
        {
            agent.Disconnect();
        }
    }

    static void Resume(MQQueueManager queueManager, string topicString, string subscriptionName)
    {
        int options = MQC.MQSO_RESUME | MQC.MQSO_FAIL_IF_QUIESCING | MQC.MQSO_DURABLE;

        using var topic = queueManager.AccessTopic(
            null,
            topicString,
            null,
            options,
            null,
            subscriptionName
        );
    }

    static void CreateDurable(MQQueueManager queueManager, string topicString, string subscriptionName, string endpointQueue)
    {
        int options = MQC.MQSO_CREATE | MQC.MQSO_FAIL_IF_QUIESCING | MQC.MQSO_DURABLE;

        var destinationQueue = queueManager.AccessQueue(endpointQueue, MQC.MQOO_OUTPUT);
        try
        {
            using var topic = queueManager.AccessTopic(
                destinationQueue,
                topicString,
                null,
                options,
                null,
                subscriptionName
            );
        }
        finally
        {
            destinationQueue.Close();
        }
    }
}
