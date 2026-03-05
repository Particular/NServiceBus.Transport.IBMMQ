namespace NServiceBus.Transport.IBMMQ.PerformanceTests.Infrastructure;

using IBM.WMQ;
using IBM.WMQ.PCF;

static class QueueOperations
{
    public static void EnsureQueueExists(MQQueueManager queueManager, string queueName, int maxDepth = 5000)
    {
        var agent = new PCFMessageAgent(queueManager);
        try
        {
            var request = new PCFMessage(MQC.MQCMD_CREATE_Q);
            request.AddParameter(MQC.MQCA_Q_NAME, queueName);
            request.AddParameter(MQC.MQIA_Q_TYPE, MQC.MQQT_LOCAL);
            request.AddParameter(MQC.MQIA_MAX_Q_DEPTH, maxDepth);
            request.AddParameter(MQC.MQIA_DEF_PERSISTENCE, MQC.MQPER_PERSISTENT);
            agent.Send(request);
        }
        catch (PCFException e) when (e.ReasonCode == MQC.MQRCCF_OBJECT_ALREADY_EXISTS)
        {
            SetMaxQueueDepth(queueManager, queueName, maxDepth);
        }
        finally
        {
            agent.Disconnect();
        }
    }

    public static void SetMaxQueueDepth(MQQueueManager queueManager, string queueName, int maxDepth)
    {
        var agent = new PCFMessageAgent(queueManager);
        try
        {
            var request = new PCFMessage(MQC.MQCMD_CHANGE_Q);
            request.AddParameter(MQC.MQCA_Q_NAME, queueName);
            request.AddParameter(MQC.MQIA_Q_TYPE, MQC.MQQT_LOCAL);
            request.AddParameter(MQC.MQIA_MAX_Q_DEPTH, maxDepth);
            agent.Send(request);
        }
        finally
        {
            agent.Disconnect();
        }
    }

    public static void PurgeQueue(MQQueueManager queueManager, string queueName)
    {
        var agent = new PCFMessageAgent(queueManager);
        try
        {
            var request = new PCFMessage(MQC.MQCMD_CLEAR_Q);
            request.AddParameter(MQC.MQCA_Q_NAME, queueName);
            agent.Send(request);
        }
        catch (PCFException)
        {
            // Queue may not exist or may already be empty
        }
        finally
        {
            agent.Disconnect();
        }
    }

    public static int GetQueueDepth(MQQueueManager queueManager, string queueName)
    {
        try
        {
            using var queue = queueManager.AccessQueue(queueName, MQC.MQOO_INQUIRE);
            return queue.CurrentDepth;
        }
        catch (MQException)
        {
            return -1;
        }
    }
}

