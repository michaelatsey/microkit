namespace MicroKit.Messaging.Registry;

using System.Reflection;

/// <summary>
/// Central registry that maps integration event types to their consuming handlers and the
/// delegates used to invoke them.
/// </summary>
/// <remarks>
/// <para>
/// Two lookup directions are maintained, and they are at different stages of their life:
/// <list type="bullet">
/// <item><description>
/// <strong>By consumer type name</strong> (<see cref="TryGetInvoker"/>) — used by
/// <c>InboxProcessor</c> to find the invoker for a specific <c>InboxMessage.ConsumerType</c>
/// during drain. Live.
/// </description></item>
/// <item><description>
/// <strong>By event type</strong> (<see cref="GetHandlers"/>) — discovers which consumers should
/// receive a message. Its only caller was the in-process fan-out, deleted with the in-process
/// transport (ADR-MSG-019), so it currently has <b>none</b>. It is kept rather than deleted
/// because it is the seam the receiving side needs once an envelope can be turned back into inbox
/// rows: a wire name resolves to a local type through <c>IntegrationEventRegistry</c>, and that
/// type resolves to its consumers here. Covered directly by <c>MessageHandlerRegistryTests</c>,
/// so it is an uncalled seam rather than untested code.
/// </description></item>
/// </list>
/// </para>
/// <para>
/// The registry is designed to be registered as a singleton and populated at startup
/// via <c>MessagingBuilder.AddMessageHandler&lt;THandler, TEvent&gt;()</c>.
/// </para>
/// </remarks>
public sealed class MessageHandlerRegistry
{
    private readonly Dictionary<Type, List<HandlerEntry>> _byEventType = new();
    private readonly Dictionary<string, HandlerEntry> _byConsumerType = new();

    /// <summary>
    /// Entry stored in the handler registry. Contains the consumer type name,
    /// the handler CLR type, and a pre-compiled invoker delegate.
    /// </summary>
    public readonly record struct HandlerEntry(
        string ConsumerType,
        Type HandlerType,
        Func<object, IIntegrationEvent, CancellationToken, ValueTask> Invoker);

    /// <summary>
    /// Registers a handler using an invoker closed over <typeparamref name="TEvent"/> at
    /// compile time. Called by <c>MessagingBuilder.AddMessageHandler&lt;THandler, TEvent&gt;()</c>.
    /// </summary>
    /// <remarks>
    /// Preferred over <see cref="Register"/>: the cast to <c>IMessageHandler&lt;TEvent&gt;</c> is
    /// resolved by the compiler rather than through reflection at registration time.
    /// </remarks>
    public void RegisterGeneric<TEvent>(string consumerType, Type handlerType)
        where TEvent : IIntegrationEvent
    {
        var entry = new HandlerEntry(
            consumerType,
            handlerType,
            (h, e, ct) => ((IMessageHandler<TEvent>)h).HandleAsync((TEvent)e, ct));

        AddEntry(typeof(TEvent), entry);
    }

    /// <summary>
    /// Registers a handler using a reflection-based invoker. Intended for test helpers
    /// that cannot supply a generic type parameter at compile time.
    /// </summary>
    public void Register(Type eventType, string consumerType, Type handlerType)
    {
        var method = typeof(MessageHandlerRegistry)
            .GetMethod(nameof(BuildReflectionInvoker), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(eventType);

        var invoker = (Func<object, IIntegrationEvent, CancellationToken, ValueTask>)method.Invoke(null, null)!;
        AddEntry(eventType, new HandlerEntry(consumerType, handlerType, invoker));
    }

    private static Func<object, IIntegrationEvent, CancellationToken, ValueTask> BuildReflectionInvoker<TEvent>()
        where TEvent : IIntegrationEvent
        => (h, e, ct) => ((IMessageHandler<TEvent>)h).HandleAsync((TEvent)e, ct);

    private void AddEntry(Type eventType, HandlerEntry entry)
    {
        if (!_byEventType.TryGetValue(eventType, out var list))
        {
            list = new List<HandlerEntry>();
            _byEventType[eventType] = list;
        }

        list.Add(entry);
        _byConsumerType[entry.ConsumerType] = entry;
    }

    /// <summary>
    /// Returns all registered handler entries for the given runtime event type.
    /// Returns an empty list when no handlers are registered.
    /// </summary>
    public IReadOnlyList<HandlerEntry> GetHandlers(Type eventType)
        => _byEventType.TryGetValue(eventType, out var list)
            ? list
            : Array.Empty<HandlerEntry>();

    /// <summary>
    /// Attempts to locate the handler entry for a specific consumer type name
    /// (as stored in <c>InboxMessage.ConsumerType</c>).
    /// </summary>
    public bool TryGetInvoker(string consumerType, out HandlerEntry entry)
        => _byConsumerType.TryGetValue(consumerType, out entry);

    /// <summary>
    /// Every distinct consumer type name registered so far. Unordered — it is a dictionary key
    /// set, and nothing here depends on the order.
    /// </summary>
    /// <remarks>
    /// Exists so <c>InboxIngestionValidator</c> can report at startup that handlers are registered
    /// while nothing produces inbox rows for them, and name them in the failure. A count alone
    /// would say a host is misconfigured without saying which registration to look at.
    /// </remarks>
    public IReadOnlyCollection<string> RegisteredConsumerTypes => _byConsumerType.Keys;
}
