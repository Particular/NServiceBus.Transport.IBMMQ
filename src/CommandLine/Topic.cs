namespace NServiceBus.Transport.IBMMQ.CommandLine;

using IBM.WMQ;
using IBM.WMQ.PCF;

static class Topic
{
    public static void Create(MQQueueManager queueManager, string topicName, string topicString)
    {
        var agent = new PCFMessageAgent(queueManager);
        try
        {
            var command = new PCFMessage(MQC.MQCMD_CREATE_TOPIC);
            command.AddParameter(MQC.MQCA_TOPIC_NAME, topicName);
            command.AddParameter(MQC.MQCA_TOPIC_STRING, topicString);
            agent.Send(command);

            Console.WriteLine($"Topic '{topicName}' (string: '{topicString}') created successfully.");
        }
        catch (PCFException e) when (e.ReasonCode == MQC.MQRCCF_OBJECT_ALREADY_EXISTS)
        {
            Console.WriteLine($"Topic '{topicName}' already exists, skipping creation.");
        }
        finally
        {
            agent.Disconnect();
        }
    }
}
