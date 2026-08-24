namespace MicroKit.Messaging;

/// <summary>
/// Marker contract for integration events published across service boundaries
/// via the transactional outbox.
/// </summary>
/// <remarks>
/// All integration events must implement this interface. Implementations should be
/// <c>sealed record</c> types named with the past-tense domain language and an
/// <c>Event</c> suffix (e.g. <c>OrderPlacedEvent</c>, not <c>OrderMessage</c>).
/// <para>
/// It extends <c>MicroKit.Domain.Events.IEvent</c>, the canonical event-taxonomy root
/// (ADR-MSG-010). It does NOT extend <c>IDomainEvent</c>, <c>INotification</c>, or any MediatR
/// type, and carries no domain-event semantics — it is a standalone transport contract.
/// </para>
/// <para>
/// <c>Abstractions</c>, <c>Core</c>, <c>EntityFrameworkCore</c> and the broker providers have
/// zero MediatR dependency, enforced by architecture tests. The single exception is the
/// <c>MicroKit.Messaging.MediatR</c> glue package, which bridges domain-event notifications
/// onto the outbox (ADR-MSG-009 carve-out).
/// </para>
/// </remarks>
public interface IIntegrationEvent : IEvent
{
    /// <summary>
    /// Gets the unique identifier of this specific event instance.
    /// Used as the message identifier in the outbox and for deduplication in the inbox.
    /// </summary>
    MessageId MessageId { get; }

    /// <summary>
    /// Gets the identifier of the tenant in whose context this event was published.
    /// </summary>
    /// <remarks>
    /// Declared non-nullable, so the compiler requires a value in nullable-enabled code.
    /// <b>Emptiness is not validated anywhere</b> — treat "must not be empty" as a requirement
    /// on your event types, not as something this library checks.
    /// <para>
    /// A single-tenant deployment has no tenant to name. Messaging must run without
    /// Multitenancy (ADR-EXEC-001), so the persisted columns
    /// (<see cref="OutboxMessage.TenantId"/>, <see cref="InboxMessage.TenantId"/>) are
    /// <b>nullable</b> and a null there is valid (ADR-MSG-008 §5). Populating a meaningful
    /// tenant id is a host responsibility; this contract does not enforce it.
    /// </para>
    /// </remarks>
    string TenantId { get; }

    /// <summary>
    /// Gets the correlation identifier linking this event to the originating request chain.
    /// <see langword="null"/> when no upstream correlation context is available.
    /// </summary>
    CorrelationId? CorrelationId { get; }

    /// <summary>
    /// Gets the causation identifier of the message that directly caused this event.
    /// <see langword="null"/> for root events originating from user commands.
    /// </summary>
    CausationId? CausationId { get; }

    /// <summary>Gets the UTC time at which this event occurred.</summary>
    DateTimeOffset OccurredOnUtc { get; }
}
