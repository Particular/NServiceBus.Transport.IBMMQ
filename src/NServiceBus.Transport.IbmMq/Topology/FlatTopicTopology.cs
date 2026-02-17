namespace NServiceBus.Transport.IbmMq;

using System.Collections.Concurrent;
using System.Reflection;

/// <summary>
/// Flat topology: one topic per concrete event type. Supports full polymorphism via subscriber-side fan-out —
/// subscribing to a base class or interface also subscribes to all concrete descendants.
/// </summary>
sealed class FlatTopicTopology(string topicPrefix) : TopicTopology
{
    readonly ConcurrentDictionary<Type, IReadOnlyList<string>> subscriptionCache = new();

    internal override IReadOnlyList<TopicDestination> GetPublishDestinations(Type eventType) =>
    [
        new TopicDestination(
            TopicNaming.GenerateTopicName(topicPrefix, eventType),
            TopicNaming.GenerateTopicString(topicPrefix, eventType))
    ];

    internal override IReadOnlyList<string> GetSubscriptionTopicStrings(Type eventType) =>
        subscriptionCache.GetOrAdd(eventType, FindSubscriptionTopicStrings);

    IReadOnlyList<string> FindSubscriptionTopicStrings(Type eventType)
    {
        var topicStrings = new List<string>
        {
            TopicNaming.GenerateTopicString(topicPrefix, eventType)
        };

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || IsSystemAssembly(assembly))
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
            }

            foreach (var type in types)
            {
                if (type != eventType && eventType.IsAssignableFrom(type))//&& IsConcrete(type)
                {
                    topicStrings.Add(TopicNaming.GenerateTopicString(topicPrefix, type));
                }
            }
        }

        if (topicStrings.Count == 0)
        {
            topicStrings.Add(TopicNaming.GenerateTopicString(topicPrefix, eventType));
        }

        return topicStrings;
    }

    static bool IsSystemAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        return name != null && (name.StartsWith("System", StringComparison.Ordinal)
            || name.StartsWith("Microsoft", StringComparison.Ordinal)
            || name.StartsWith("mscorlib", StringComparison.Ordinal)
            || name.StartsWith("netstandard", StringComparison.Ordinal));
    }
}
