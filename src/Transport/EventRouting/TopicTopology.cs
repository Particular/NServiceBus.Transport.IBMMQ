namespace NServiceBus.Transport.IBMMQ;

using System.Collections.Concurrent;
using System.Reflection;
using Logging;

/// <summary>
/// Controls how events are mapped to IBM MQ topics for pub/sub.
/// One topic per concrete event type. Subscribes only to the exact type specified —
/// no automatic subscriber-side fan-out to descendant types.
/// </summary>
public sealed class TopicTopology
{
    static readonly ILog log = LogManager.GetLogger<TopicTopology>();

    readonly ConcurrentDictionary<Type, IReadOnlyList<string>> subscriptionCache = new();
    readonly ConcurrentDictionary<Type, IReadOnlyList<TopicDestination>> publishCache = new();

    /// <summary>
    /// When enabled (the default), subscribing to a type that has known descendant types
    /// (subclasses or implementors) in the loaded assemblies will throw an
    /// <see cref="InvalidOperationException"/>. This prevents accidental under-subscription
    /// where a handler for a base type or interface silently only receives messages published
    /// to that exact type's topic, missing messages published as concrete descendants.
    /// <para>
    /// This check is bypassed when explicit subscription routes are configured for the subscribed type.
    /// </para>
    /// <para>
    /// Disable this only if you intentionally want to subscribe to a single type's topic
    /// without receiving messages published as descendant types.
    /// </para>
    /// </summary>
    public bool ThrowOnPolymorphicSubscription { get; set; } = true;

    internal TopicNaming Naming { get; set; } = new();

    /// <summary>
    /// Configures the subscriber to create a subscription on <typeparamref name="TTopicType"/>'s topic
    /// when subscribing to <typeparamref name="TEventType"/>, using the default naming convention
    /// to derive the topic string. Call multiple times to subscribe to multiple topics.
    /// </summary>
    /// <example>
    /// <code>
    /// topology.SubscribeTo&lt;IOrderStatusChanged, OrderAccepted&gt;();
    /// topology.SubscribeTo&lt;IOrderStatusChanged, OrderDeclined&gt;();
    /// </code>
    /// </example>
    /// <typeparam name="TEventType">The event type being subscribed to (typically an interface or base class).</typeparam>
    /// <typeparam name="TTopicType">The type whose topic to subscribe to (typically a concrete event type).</typeparam>
    public void SubscribeTo<TEventType, TTopicType>() where TTopicType : TEventType => SubscribeTo(typeof(TEventType), typeof(TTopicType));

    /// <summary>
    /// Non-generic overload. See <see cref="SubscribeTo{TEventType, TTopicType}"/>.
    /// </summary>
    public void SubscribeTo(Type eventType, Type topicType)
    {
        ArgumentNullException.ThrowIfNull(topicType);
        SubscribeTo(eventType, Naming.GenerateTopicString(topicType));
    }

    /// <summary>
    /// Configures the subscriber to create a subscription on the specified topic string
    /// when subscribing to <typeparamref name="TEventType"/>. Call multiple times to subscribe to
    /// multiple topics for the same event type.
    /// </summary>
    /// <example>
    /// <code>
    /// topology.SubscribeTo&lt;IOrderStatusChanged&gt;("prod/orders/orderaccepted/");
    /// topology.SubscribeTo&lt;IOrderStatusChanged&gt;("prod/orders/orderdeclined/");
    /// </code>
    /// </example>
    /// <typeparam name="TEventType">The event type being subscribed to.</typeparam>
    /// <param name="topicString">The IBM MQ topic string to subscribe to.</param>
    public void SubscribeTo<TEventType>(string topicString) => SubscribeTo(typeof(TEventType), topicString);

    /// <summary>
    /// Non-generic overload. See <see cref="SubscribeTo{TEventType}(string)"/>.
    /// </summary>
    public void SubscribeTo(Type eventType, string topicString)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(topicString);

        if (!SubscribeRoutes.TryGetValue(eventType, out var topics))
        {
            topics = [];
            SubscribeRoutes[eventType] = topics;
        }

        topics.Add(topicString);
    }

    /// <summary>
    /// Adds a publish route for <typeparamref name="TEventType"/> to the specified topic string.
    /// The admin topic object name is derived from the topic string automatically.
    /// Call multiple times to publish to multiple topics.
    /// </summary>
    /// <example>
    /// <code>
    /// topology.PublishTo&lt;OrderAccepted&gt;("legacy/order/events/");
    /// </code>
    /// </example>
    /// <typeparam name="TEventType">The concrete event type being published.</typeparam>
    /// <param name="topicString">The IBM MQ topic string to publish to.</param>
    public void PublishTo<TEventType>(string topicString) => PublishTo(typeof(TEventType), topicString);

    /// <summary>
    /// Non-generic overload. See <see cref="PublishTo{TEventType}(string)"/>.
    /// </summary>
    public void PublishTo(Type eventType, string topicString)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(topicString);
        AddPublishRoute(eventType, new TopicDestination(Naming.DeriveTopicName(topicString), topicString));
    }

    /// <summary>
    /// Adds a publish route for <typeparamref name="TEventType"/> with an explicit admin topic object
    /// name and topic string. Use this when the admin object name cannot be derived from the topic string.
    /// Call multiple times to publish to multiple topics.
    /// </summary>
    /// <example>
    /// <code>
    /// topology.PublishTo&lt;OrderAccepted&gt;("LEGACY.ORDER.EVENTS", "legacy/order/events/");
    /// </code>
    /// </example>
    /// <typeparam name="TEventType">The concrete event type being published.</typeparam>
    /// <param name="topicName">The IBM MQ admin topic object name (max 48 characters).</param>
    /// <param name="topicString">The IBM MQ topic string for message routing.</param>
    public void PublishTo<TEventType>(string topicName, string topicString) => PublishTo(typeof(TEventType), topicName, topicString);

    /// <summary>
    /// Non-generic overload. See <see cref="PublishTo{TEventType}(string, string)"/>.
    /// </summary>
    public void PublishTo(Type eventType, string topicName, string topicString)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(topicName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topicString);
        AddPublishRoute(eventType, new TopicDestination(topicName, topicString));
    }

    void AddPublishRoute(Type eventType, TopicDestination destination)
    {
        if (!PublishRoutes.TryGetValue(eventType, out var topics))
        {
            topics = [];
            PublishRoutes[eventType] = topics;
        }

        topics.Add(destination);
    }

    internal Dictionary<Type, List<string>> SubscribeRoutes { get; } = [];
    internal Dictionary<Type, List<TopicDestination>> PublishRoutes { get; } = [];

    internal IReadOnlyList<TopicDestination> GetPublishDestinations(Type eventType) =>
        publishCache.GetOrAdd(eventType, static (type, self) =>
        {
            if (self.PublishRoutes.TryGetValue(type, out var routes))
            {
                return routes.AsReadOnly();
            }

            return
            [
                new TopicDestination(
                    self.Naming.GenerateTopicName(type),
                    self.Naming.GenerateTopicString(type))
            ];
        }, this);

    internal IReadOnlyList<string> GetSubscriptionTopicStrings(Type eventType)
    {
        if (SubscribeRoutes.TryGetValue(eventType, out var routes))
        {
            return subscriptionCache.GetOrAdd(eventType, static (_, r) => r.AsReadOnly(), routes);
        }

        if (ThrowOnPolymorphicSubscription)
        {
            try
            {
                var descendants = FindDescendantTypes(eventType);
                if (descendants.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"""
                        Cannot subscribe to '{eventType.FullName}' because it has {descendants.Count} known descendant type(s) in the loaded assemblies: [{string.Join(", ", descendants.Select(t => t.FullName))}].
                        Subscribing to a base type does not automatically subscribe to its descendants.
                        Use {nameof(TopicTopology)}.{nameof(SubscribeTo)}() to explicitly map which concrete types' topics to subscribe to, or disable this check by setting {nameof(TopicTopology)}.{nameof(ThrowOnPolymorphicSubscription)} = false.
                        """);
                }
            }
            catch (Exception e) when (e is not InvalidOperationException)
            {
                log.Error($"Failed to determine if type {eventType} has descendant types", e);
            }
        }

        return subscriptionCache.GetOrAdd(eventType, static (type, naming) =>
            [naming.GenerateTopicString(type)], Naming);
    }

    internal IEnumerable<TopicDestination> GetExplicitTopicDestinations() =>
        PublishRoutes.Values.SelectMany(r => r)
            .Concat(SubscribeRoutes.Values
                .SelectMany(r => r)
                .Select(ts => new TopicDestination(Naming.DeriveTopicName(ts), ts)))
            .DistinctBy(d => d.TopicString, StringComparer.Ordinal);

    internal string GenerateSubscriptionName(string endpointName, string topicString) =>
        Naming.GenerateSubscriptionName(endpointName, topicString);

    static List<Type> FindDescendantTypes(Type eventType)
    {
        var descendants = new List<Type>();

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
                if (type != eventType && eventType.IsAssignableFrom(type))
                {
                    descendants.Add(type);
                }
            }
        }

        return descendants;
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
