namespace NServiceBus.Transport.IBMMQ;

using Logging;

sealed class TopologyCreator(ILog log, TopicTopology topology, MqAdminConnection admin)
{
    public void Create()
    {
        foreach (var destination in topology.GetExplicitTopicDestinations())
        {
            if (destination.TopicName is not null)
            {
                log.DebugFormat("Creating topic {0} ({1})", destination.TopicName, destination.TopicString);
                admin.CreateTopic(destination.TopicName, destination.TopicString);
            }
        }
    }
}
