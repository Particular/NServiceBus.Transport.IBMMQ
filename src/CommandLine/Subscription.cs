namespace NServiceBus.Transport.IbmMq.CommandLine;

using IBM.WMQ;
using IBM.WMQ.PCF;

static class Subscription
{
    public static void Create(CommandRunner runner, string topicString, string subscriptionName, string endpointQueue)
    {
        try
        {
            AccessSubscription(runner, topicString, subscriptionName, endpointQueue, MQC.MQSO_RESUME);

            Console.WriteLine($"Subscription '{subscriptionName}' already exists, skipping creation.");
        }
        catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_SUBSCRIPTION)
        {
            AccessSubscription(runner, topicString, subscriptionName, endpointQueue, MQC.MQSO_CREATE);

            Console.WriteLine($"Subscription '{subscriptionName}' created successfully.");
        }
    }

    public static void Delete(CommandRunner runner, string subscriptionName)
    {
        var agent = new PCFMessageAgent(runner.QueueManager);
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

    static void AccessSubscription(CommandRunner runner, string topicString, string subscriptionName, string endpointQueue, int options)
    {
        int finalOptions = options
                           | MQC.MQSO_FAIL_IF_QUIESCING
                           | MQC.MQSO_DURABLE;

        if (options == MQC.MQSO_CREATE)
        {
            var destinationQueue = runner.QueueManager.AccessQueue(endpointQueue, MQC.MQOO_OUTPUT);
            try
            {
                using var topic = runner.QueueManager.AccessTopic(
                    destinationQueue,
                    topicString,
                    null,
                    finalOptions,
                    null,
                    subscriptionName
                );
            }
            finally
            {
                destinationQueue.Close();
            }

            return;
        }

        using var resumedTopic = runner.QueueManager.AccessTopic(
            null,
            topicString,
            null,
            finalOptions,
            null,
            subscriptionName
        );
    }
}
