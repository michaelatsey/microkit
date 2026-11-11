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
/// <strong>By consumer type name and message type</strong> (<see cref="TryGetInvoker"/>) — used
/// by <c>InboxProcessor</c> to find the invoker for a row's <c>ConsumerType</c> and
/// <c>EventType</c> during drain. The pair, not the consumer alone (#187): one handler may
/// implement <c>IMessageHandler&lt;T&gt;</c> for several types, and each type has its own invoker.
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
/// via <c>bus.AddHandler&lt;THandler&gt;()</c> and <c>bus.AddHandlersFromAssemblyContaining&lt;T&gt;()</c>,
/// which register one entry per closed <c>IMessageHandler&lt;T&gt;</c> the handler implements.
/// </para>
/// </remarks>
internal sealed class MessageHandlerRegistry
{
    private readonly Dictionary<Type, List<HandlerEntry>> _byEventType = new();
    private readonly Dictionary<(string ConsumerType, Type EventType), HandlerEntry> _byConsumer = new();

    /// <summary>
    /// Entry stored in the handler registry. Contains the consumer type name,
    /// the handler CLR type, and a pre-compiled invoker delegate.
    /// </summary>
    public readonly record struct HandlerEntry(
        string ConsumerType,
        Type HandlerType,
        Func<object, IIntegrationEvent, MessageContext, CancellationToken, ValueTask> Invoker);

    /// <summary>
    /// Registers a handler using an invoker closed over <typeparamref name="TEvent"/> at
    /// compile time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a message type known at compile time — the tests' fixtures. Production registration
    /// goes through <see cref="Register"/> since #187: handler discovery learns each handled type
    /// from the handler's interfaces at run time. Both build the same invoker and share one
    /// insertion path.
    /// </para>
    /// <para>
    /// <b>Idempotent in (<paramref name="consumerType"/>, <typeparamref name="TEvent"/>).</b>
    /// Registering the same consumer twice for one type contributes one consumer, not two, and the
    /// later registration replaces the earlier — see <see cref="GetHandlers"/> for why appending
    /// would misreport a first delivery as a redelivery.
    /// </para>
    /// </remarks>
    public void RegisterGeneric<TEvent>(string consumerType, Type handlerType)
        where TEvent : IIntegrationEvent
    {
        var entry = new HandlerEntry(
            consumerType,
            handlerType,
            (h, e, c, ct) => ((IMessageHandler<TEvent>)h).HandleAsync((TEvent)e, c, ct));

        AddEntry(typeof(TEvent), entry);
    }

    /// <summary>
    /// Registers a handler using a reflection-based invoker, for a message type known only at run
    /// time — the handler discovery of <c>bus.AddHandler&lt;THandler&gt;()</c> (#187).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The invoker is built once, at registration, by closing a generic method over the message
    /// type (<c>MakeGenericMethod</c>); the delivery path then calls a typed delegate, with no
    /// reflection. That closing is trim-safe — the generic parameter carries no
    /// <c>DynamicallyAccessedMembers</c> requirement — but it needs dynamic code, so it is not
    /// Native AOT-safe; the module claims trimmability (<c>IsTrimmable</c>), not AOT compatibility.
    /// </para>
    /// <b>Idempotent in (<paramref name="consumerType"/>, <paramref name="eventType"/>)</b>, exactly as
    /// <see cref="RegisterGeneric{TEvent}"/> is — the two share one insertion path. A repeated
    /// registration replaces the earlier entry rather than appending a second; see
    /// <see cref="GetHandlers"/>.
    /// </remarks>
    public void Register(Type eventType, string consumerType, Type handlerType)
    {
        var method = typeof(MessageHandlerRegistry)
            .GetMethod(nameof(BuildReflectionInvoker), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(eventType);

        var invoker = (Func<object, IIntegrationEvent, MessageContext, CancellationToken, ValueTask>)method.Invoke(null, null)!;
        AddEntry(eventType, new HandlerEntry(consumerType, handlerType, invoker));
    }

    private static Func<object, IIntegrationEvent, MessageContext, CancellationToken, ValueTask> BuildReflectionInvoker<TEvent>()
        where TEvent : IIntegrationEvent
        => (h, e, c, ct) => ((IMessageHandler<TEvent>)h).HandleAsync((TEvent)e, c, ct);

    /// <summary>
    /// Adds one entry, idempotently in (<c>ConsumerType</c>, message type). A repeated registration
    /// of the same handler for the same type replaces its entry rather than appending a second; a
    /// registration of the same handler for ANOTHER type is a different entry and replaces nothing.
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
    /// <see cref="_byConsumer"/> has always overwritten, and a re-registration carries a
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

        _byConsumer[(entry.ConsumerType, eventType)] = entry;
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
    /// Attempts to locate the handler entry for a consumer type name and a message type (as stored
    /// in <c>InboxMessage.ConsumerType</c> and resolved from <c>InboxMessage.EventType</c>).
    /// </summary>
    /// <param name="consumerType">The consumer type name.</param>
    /// <param name="eventType">The local message type.</param>
    /// <param name="entry">The entry, when one is registered for the pair.</param>
    /// <returns><see langword="true"/> when the pair is registered.</returns>
    public bool TryGetInvoker(string consumerType, Type eventType, out HandlerEntry entry)
        => _byConsumer.TryGetValue((consumerType, eventType), out entry);

    /// <summary>
    /// Every distinct consumer type name registered so far. Unordered — it is a dictionary key
    /// set, and nothing here depends on the order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Distinct by consumer type.</b> Entries are keyed on (<c>ConsumerType</c>, message type)
    /// since #187, so a handler of two message types holds two entries; this set names it once.
    /// A handler registered twice for one type appears once too — see <see cref="GetHandlers"/>.
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
    public IReadOnlyCollection<string> RegisteredConsumerTypes =>
        [.. _byConsumer.Keys.Select(k => k.ConsumerType).Distinct(StringComparer.Ordinal)];
}
