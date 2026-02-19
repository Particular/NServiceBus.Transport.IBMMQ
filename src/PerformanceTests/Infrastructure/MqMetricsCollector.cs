namespace NServiceBus.Transport.IbmMq.PerformanceTests.Infrastructure;

using IBM.WMQ;
using IBM.WMQ.PCF;

static class MqMetricsCollector
{
    public static int GetQueueDepth(MQQueueManager queueManager, string queueName)
    {
        var agent = new PCFMessageAgent(queueManager);
        try
        {
            var request = new PCFMessage(MQC.MQCMD_INQUIRE_Q);
            request.AddParameter(MQC.MQCA_Q_NAME, queueName);
            request.AddParameter(MQC.MQIA_Q_TYPE, MQC.MQQT_LOCAL);
            request.AddParameter(MQC.MQIACF_Q_ATTRS, new[] { MQC.MQIA_CURRENT_Q_DEPTH });

            var responses = agent.Send(request);
            if (responses.Length > 0)
            {
                return responses[0].GetIntParameterValue(MQC.MQIA_CURRENT_Q_DEPTH);
            }

            return -1;
        }
        catch (PCFException)
        {
            return -1;
        }
        finally
        {
            agent.Disconnect();
        }
    }

    public static (int EnqCount, int DeqCount) ResetQueueStats(MQQueueManager queueManager, string queueName)
    {
        var agent = new PCFMessageAgent(queueManager);
        try
        {
            var request = new PCFMessage(MQC.MQCMD_RESET_Q_STATS);
            request.AddParameter(MQC.MQCA_Q_NAME, queueName);

            var responses = agent.Send(request);
            if (responses.Length > 0)
            {
                var enqCount = responses[0].GetIntParameterValue(MQC.MQIA_MSG_ENQ_COUNT);
                var deqCount = responses[0].GetIntParameterValue(MQC.MQIA_MSG_DEQ_COUNT);
                return (enqCount, deqCount);
            }

            return (0, 0);
        }
        catch (PCFException)
        {
            return (0, 0);
        }
        finally
        {
            agent.Disconnect();
        }
    }

    public static string GetChannelStatus(MQQueueManager queueManager, string channelName = "DEV.ADMIN.SVRCONN")
    {
        var agent = new PCFMessageAgent(queueManager);
        try
        {
            var request = new PCFMessage(MQC.MQCMD_INQUIRE_CHANNEL_STATUS);
            request.AddParameter(MQC.MQCACH_CHANNEL_NAME, channelName);

            var responses = agent.Send(request);
            if (responses.Length > 0)
            {
                var status = responses[0].GetIntParameterValue(MQC.MQIACH_CHANNEL_STATUS);
                return status switch
                {
                    MQC.MQCHS_RUNNING => "Running",
                    MQC.MQCHS_STOPPED => "Stopped",
                    MQC.MQCHS_BINDING => "Binding",
                    MQC.MQCHS_STARTING => "Starting",
                    MQC.MQCHS_STOPPING => "Stopping",
                    MQC.MQCHS_INACTIVE => "Inactive",
                    _ => $"Unknown({status})"
                };
            }

            return "Unknown";
        }
        catch (PCFException)
        {
            return "Unavailable";
        }
        finally
        {
            agent.Disconnect();
        }
    }
}
