namespace NServiceBus.Transport.IbmMq.CommandLine;

static class Endpoint
{
    public static async Task Create(CommandRunner runner, string name)
    {
        using (runner)
        {
            await Queue.Create(runner, name).ConfigureAwait(false);
        }
    }

    public static Task Subscribe(CommandRunner runner, string endpointName, string eventTypeName, string topicPrefix)
    {
        using (runner)
        {
            var topicName = TopicNaming.GenerateTopicName(eventTypeName, topicPrefix);
            var topicString = TopicNaming.GenerateTopicString(eventTypeName, topicPrefix);
            var subscriptionName = TopicNaming.GenerateSubscriptionName(endpointName, topicString);

            Topic.Create(runner, topicName, topicString);
            Subscription.Create(runner, topicString, subscriptionName, endpointName);
        }

        return Task.CompletedTask;
    }

    public static Task Unsubscribe(CommandRunner runner, string endpointName, string eventTypeName, string topicPrefix)
    {
        using (runner)
        {
            var topicString = TopicNaming.GenerateTopicString(eventTypeName, topicPrefix);
            var subscriptionName = TopicNaming.GenerateSubscriptionName(endpointName, topicString);

            Subscription.Delete(runner, subscriptionName);
        }

        return Task.CompletedTask;
    }
}
