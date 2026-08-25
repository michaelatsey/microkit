namespace MicroKit.Messaging;

/// <summary>
/// The dependency-injection service keys under which <see cref="IOutboxDispatcher"/>
/// implementations are registered.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two slots, two roles.</b> The <i>keyed</i> slot named by <see cref="Standard"/> is where the
/// dispatcher that serves <see cref="MessageKind.Contract"/> rows lives, and only
/// <c>MicroKit.Messaging</c> ever writes it. The <i>unkeyed</i> <see cref="IOutboxDispatcher"/> slot
/// is the seam <c>OutboxProcessor</c> resolves, and a decorating package takes it outright.
/// Separating the two is what removes the contest: without it, two packages append to one slot,
/// Microsoft DI resolves the last registration, and which one wins depends on the order the
/// composition root happened to call them in.
/// </para>
/// <para>
/// That is not a hypothetical. Under a plain <c>Add</c>, calling a transport registration
/// <i>after</i> the MediatR glue appended a second descriptor and the decorator was bypassed with no
/// exception and no log — the outbox kept draining and nothing it routed was ever published
/// (ADR-MSG-016). <c>TryAdd</c> closed that direction and opened the mirror image: with the
/// decorator already holding the one slot, a later transport registration abstained and was never
/// registered at all. A key removes both.
/// </para>
/// <para>
/// <b>The string is a compatibility commitment, not an implementation detail.</b> A decorator in one
/// package resolves its inner by this value from another package. Changing it unlinks the two with
/// no compile error and no startup failure: the decorator simply finds nothing, and every
/// <see cref="MessageKind.Contract"/> row starts failing as a configuration fault in a deployment
/// where nothing about the composition changed.
/// </para>
/// </remarks>
public static class OutboxDispatcherKeys
{
    /// <summary>
    /// The key for the standard dispatcher — the one that serves
    /// <see cref="MessageKind.Contract"/> rows by handing a <see cref="MessageEnvelope"/> to
    /// <see cref="IMessageTransport"/>. Registered by <c>MessagingBuilder.AddTransportDispatcher()</c>.
    /// </summary>
    /// <remarks>
    /// Resolve it with <c>GetKeyedService</c> rather than <c>GetRequiredKeyedService</c> when a
    /// missing registration is a legal composition: a host that publishes only domain-event
    /// notifications registers no transport dispatcher at all, and a decorator over this seam must
    /// tolerate a <see langword="null"/> inner rather than refuse to be constructed.
    /// </remarks>
    public const string Standard = "microkit.messaging.outbox-dispatcher.standard";
}
