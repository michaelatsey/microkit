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
/// This interface does NOT extend <c>IDomainEvent</c>, <c>INotification</c>, or any
/// MediatR type. <c>MicroKit.Messaging</c> has zero MediatR dependency (ADR-MSG-001).
/// </para>
/// </remarks>
public interface IIntegrationEvent
{
    /// <summary>
    /// Gets the unique identifier of this specific event instance.
    /// Used as the message identifier in the outbox and for deduplication in the inbox.
    /// </summary>
    MessageId MessageId { get; }

    /// <summary>
    /// Gets the identifier of the tenant in whose context this event was published.
    /// Required — never <see langword="null"/> or empty.
    /// </summary>
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
