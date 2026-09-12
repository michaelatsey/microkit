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
/// receive a message. Used by <c>EnvelopeReceiver</c> to fan one arriving
/// <c>MessageEnvelope</c> out into one inbox row per consumer. ADR-MSG-019 kept this direction
/// alive through the step that had no caller for it, on the argument that the receiving side
/// would need it; it does, and this is it. A wire name resolves to a local type through
/// <c>IntegrationEventRegistry</c>, and that type resolves to its consumers here.
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
    /// <para>
    /// Preferred over <see cref="Register"/>: the cast to <c>IMessageHandler&lt;TEvent&gt;</c> is
    /// resolved by the compiler rather than through reflection at registration time.
    /// </para>
    /// <para>
    /// <b>Idempotent in <paramref name="consumerType"/>.</b> Registering the same consumer twice
    /// contributes one consumer, not two, and the later registration replaces the earlier — see
    /// <see cref="GetHandlers"/> for why appending would misreport a first delivery as a
    /// redelivery.
    /// </para>
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
    /// <remarks>
    /// <b>Idempotent in <paramref name="consumerType"/></b>, exactly as
    /// <see cref="RegisterGeneric{TEvent}"/> is — the two share one insertion path. A repeated
    /// registration replaces the earlier entry rather than appending a second; see
    /// <see cref="GetHandlers"/>.
    /// </remarks>
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

    /// <summary>
    /// Adds one entry, idempotently in <c>ConsumerType</c>. A repeated registration of the same
    /// handler replaces its entry rather than appending a second.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Registering the same handler twice must contribute one consumer, not two.</b> It is an
    /// ordinary mistake in a composition root assembled from several module registrations, and
    /// appending would make <c>EnvelopeReceiver</c> fan one envelope out to the same consumer
    /// twice: the second write hits the dedup index, so a <i>first</i> delivery reports
    /// <c>Duplicates = 1</c> and increments <c>microkit.inbox.messages.deduplicated</c> — the
    /// counter whose documented use is spotting a lease set too short or a broker replaying. A
    /// composition typo would read as a broker fault, permanently, at a steady rate.
    /// </para>
    /// <para>
    /// Replace rather than ignore, so the two dictionaries cannot disagree:
    /// <see cref="_byConsumerType"/> has always overwritten, and a re-registration carries a
    /// freshly built invoker that must be the one both lookups return.
    /// </para>
    /// </remarks>
    private void AddEntry(Type eventType, HandlerEntry entry)
    {
        if (!_byEventType.TryGetValue(eventType, out var list))
        {
            list = new List<HandlerEntry>();
            _byEventType[eventType] = list;
        }

        var existing = list.FindIndex(
            e => string.Equals(e.ConsumerType, entry.ConsumerType, StringComparison.Ordinal));

        if (existing >= 0)
        {
            list[existing] = entry;
        }
        else
        {
            list.Add(entry);
        }

        _byConsumerType[entry.ConsumerType] = entry;
    }

    /// <summary>
    /// Returns all registered handler entries for the given runtime event type.
    /// Returns an empty list when no handlers are registered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>At most one entry per <c>ConsumerType</c>.</b> Registration deduplicates on it, so a
    /// handler registered twice appears once here. That is not a convenience — it is what keeps
    /// the fan-out honest. <c>EnvelopeReceiver</c> writes one inbox row per entry returned, so a
    /// doubled entry would send the same consumer two writes for one envelope: the second hits the
    /// <c>(MessageId, ConsumerType)</c> unique index, so a <b>first</b> delivery would report
    /// <c>Duplicates = 1</c> and increment <c>microkit.inbox.messages.deduplicated</c> — the
    /// counter whose documented use is spotting a lease set too short or a broker replaying. A
    /// composition typo would read as a broker fault, permanently, at a steady rate.
    /// </para>
    /// <para>
    /// A repeated registration <i>replaces</i> rather than being ignored, so that this lookup and
    /// <see cref="TryGetInvoker"/> cannot return different invokers for one consumer type: the
    /// consumer-type index has always overwritten, and a re-registration carries a freshly built
    /// invoker that must be the one both directions return.
    /// </para>
    /// <para>
    /// Ordering is the insertion order of first registration and nothing depends on it.
    /// </para>
    /// </remarks>
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
    /// <para>
    /// <b>Distinct by construction, not by filtering.</b> Registration deduplicates on
    /// <c>ConsumerType</c>, so a handler registered twice appears once here and the count is the
    /// number of consumers this host will actually fan out to — see <see cref="GetHandlers"/>.
    /// </para>
    /// It was written for <c>InboxIngestionValidator</c>, which named the registered consumers in
    /// its boot failure — and that validator was deleted with the receiving seam, so this property
    /// now has no production caller. Kept rather than removed, for diagnostics and for the same
    /// reason ADR-MSG-019 kept <see cref="GetHandlers"/> through the step that had no caller for
    /// it: a member that answers "which consumers did this host actually register" is what an
    /// operator needs when <c>EnvelopeReceiver</c> reports a contract nothing consumes. Covered
    /// directly by <c>MessageHandlerRegistryTests</c>, so it is an uncalled seam rather than
    /// untested code. Worth revisiting at 1.0.0, not before.
    /// </remarks>
    public IReadOnlyCollection<string> RegisteredConsumerTypes => _byConsumerType.Keys;
}
