namespace MicroKit.Domain.Events;

/// <summary>
/// Abstract base for domain events. Declare concrete events as sealed positional records
/// that repeat <paramref name="OccurredAt"/> as their last parameter and forward it to
/// this base.
/// </summary>
/// <param name="OccurredAt">
/// The instant the business fact occurred. Pass the same value to every event raised within one
/// business operation so consumers can group and order them reliably.
/// </param>
/// <remarks>
/// <para>
/// The instant is a positional parameter with no default. It is an input of the operation
/// that produced the fact, not something the event discovers on its own: a default read
/// from the system clock would make the value optional at every construction
/// site, and events belonging to one transaction would drift apart by milliseconds with
/// nothing to signal it. Declaring it positionally moves that failure to the compiler while
/// keeping concrete events to a single line.
/// </para>
/// <para>
/// <see cref="EventId"/> is not positional because it carries no business meaning: it only
/// has to be unique, and a default keeps event declarations free of ceremony. The <c>init</c>
/// accessor stays public so that replay, import and migration can preserve an original
/// identity, which is what makes downstream deduplication work.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed record OrderShipped(
///     OrderId OrderId, string TrackingNumber, DateTimeOffset OccurredAt)
///     : DomainEvent(OccurredAt);
///
/// // raising it, inside the aggregate
/// RaiseDomainEvent(new OrderShipped(Id, tracking, now));
///
/// // re-materializing an existing event, preserving its identity
/// var replayed = new OrderShipped(orderId, tracking, original.OccurredAt)
/// {
///     EventId = original.EventId
/// };
/// </code>
/// </example>
public abstract record DomainEvent(DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <summary>
    /// Gets the instant the business fact occurred, at offset zero.
    /// </summary>
    /// <remarks>
    /// The instant is normalised to offset zero with <see cref="DateTimeOffset.ToUniversalTime"/>
    /// on construction and through a <see langword="with"/> expression: the instant is preserved,
    /// only its representation changes. <c>default(DateTimeOffset)</c> is rejected with
    /// <see cref="ArgumentOutOfRangeException"/> on both paths.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the instant is <c>default(DateTimeOffset)</c>.
    /// </exception>
    public DateTimeOffset OccurredAt
    {
        get;
        init => field = UtcInstant.Normalize(value, nameof(OccurredAt));
    } = UtcInstant.Normalize(OccurredAt, nameof(OccurredAt));

    /// <summary>
    /// Gets the unique, time-ordered identifier for this domain event.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default is a UUID version 7 from <see cref="Guid.CreateVersion7()"/>. Domain events
    /// are routinely persisted in an outbox keyed by this identifier: a time-ordered key keeps
    /// the index compact, where a random version 4 key fragments it, and makes ordering by
    /// identifier follow creation order.
    /// </para>
    /// <para>
    /// That ordering holds where the database compares the value as a byte sequence, as
    /// PostgreSQL does for <c>uuid</c>. SQL Server orders <c>uniqueidentifier</c> by a different
    /// byte order, so a version 7 value is not sequential there, though no worse than a
    /// version 4 one. EF Core's <c>SequentialGuidValueGenerator</c> does not help: it runs only
    /// for keys EF Core generates, never for an identifier the domain already assigned.
    /// </para>
    /// </remarks>
    public Guid EventId { get; init; } = Guid.CreateVersion7();
}
