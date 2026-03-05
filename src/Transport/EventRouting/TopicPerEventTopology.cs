namespace NServiceBus.Transport.IBMMQ;

using System.Collections.Concurrent;
using System.Reflection;

/// <summary>
/// One topic per concrete event type. Supports full polymorphism via subscriber-side fan-out —
/// subscribing to a base class or interface also subscribes to all concrete descendants.
/// </summary>
sealed class TopicPerEventTopology : TopicTopology
{
    readonly ConcurrentDictionary<Type, IReadOnlyList<string>> subscriptionCache = new();

    internal override IReadOnlyList<TopicDestination> GetPublishDestinations(Type eventType) =>
    [
        new TopicDestination(
            Naming.GenerateTopicName(eventType),
            Naming.GenerateTopicString(eventType))
    ];

    internal override IReadOnlyList<string> GetSubscriptionTopicStrings(Type eventType) =>
        subscriptionCache.GetOrAdd(eventType, FindSubscriptionTopicStrings);

    IReadOnlyList<string> FindSubscriptionTopicStrings(Type eventType)
    {
        var topicStrings = new List<string>
        {
            Naming.GenerateTopicString(eventType)
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
                    topicStrings.Add(Naming.GenerateTopicString(type));
                }
            }
        }

        if (topicStrings.Count == 0)
        {
            topicStrings.Add(Naming.GenerateTopicString(eventType));
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
