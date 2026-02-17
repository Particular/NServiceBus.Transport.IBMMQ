namespace NServiceBus.Transport.IbmMq.CommandLine;

using IBM.WMQ;
using IBM.WMQ.PCF;

static class Queue
{
    public static Task Create(CommandRunner runner, string name, int maxDepth = 5000)
    {
        var agent = new PCFMessageAgent(runner.QueueManager);
        try
        {
            var request = new PCFMessage(MQC.MQCMD_CREATE_Q);
            request.AddParameter(MQC.MQCA_Q_NAME, name);
            request.AddParameter(MQC.MQIA_Q_TYPE, MQC.MQQT_LOCAL);
            request.AddParameter(MQC.MQIA_MAX_Q_DEPTH, maxDepth);
            request.AddParameter(MQC.MQIA_DEF_PERSISTENCE, MQC.MQPER_PERSISTENT);

            agent.Send(request);

            Console.WriteLine($"Queue '{name}' created successfully.");
        }
        catch (PCFException e) when (e.ReasonCode == MQC.MQRCCF_OBJECT_ALREADY_EXISTS)
        {
            Console.WriteLine($"Queue '{name}' already exists, skipping creation.");
        }
        finally
        {
            agent.Disconnect();
        }

        return Task.CompletedTask;
    }

    public static Task Delete(CommandRunner runner, string name)
    {
        var agent = new PCFMessageAgent(runner.QueueManager);
        try
        {
            var request = new PCFMessage(MQC.MQCMD_DELETE_Q);
            request.AddParameter(MQC.MQCA_Q_NAME, name);

            agent.Send(request);

            Console.WriteLine($"Queue '{name}' deleted successfully.");
        }
        catch (PCFException e) when (e.ReasonCode == MQC.MQRCCF_UNKNOWN_OBJECT_NAME)
        {
            Console.WriteLine($"Queue '{name}' does not exist, skipping deletion.");
        }
        finally
        {
            agent.Disconnect();
        }

        return Task.CompletedTask;
    }
}
