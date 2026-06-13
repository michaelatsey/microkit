namespace MicroKit.Messaging;

/// <summary>
/// Represents an inbound message stored in the transactional inbox, used to
/// guarantee idempotent, at-most-once handler invocation per consumer.
/// </summary>
/// <remarks>
/// The compound key <c>(MessageId, ConsumerType)</c> is the authoritative deduplication
/// guard, enforced by a unique database constraint. A single envelope may be consumed
/// by multiple handlers independently — each gets its own <see cref="InboxMessage"/> row.
/// <para>
/// <c>InboxMessage</c> is a <c>sealed class</c> because EF Core change tracking
/// requires mutable <c>{ get; set; }</c> properties.
/// </para>
/// <para>
/// <c>TenantId</c> is mandatory — never <see langword="null"/> or empty.
/// </para>
/// </remarks>
public sealed class InboxMessage
{
    /// <summary>
    /// Gets or sets the identifier of the original message.
    /// Part 1 of the compound deduplication key — paired with <see cref="ConsumerType"/>.
    /// </summary>
    public MessageId MessageId { get; set; } = null!;

    /// <summary>
    /// Gets or sets the assembly-qualified CLR type name of the handler consuming this message
    /// (e.g. <c>"MyApp.Orders.OrderPlacedHandler, MyApp"</c>).
    /// Part 2 of the compound deduplication key.
    /// </summary>
    public string ConsumerType { get; set; } = null!;

    /// <summary>
    /// Gets or sets the identifier of the tenant this message belongs to.
    /// Required — never <see langword="null"/> or empty.
    /// </summary>
    public string TenantId { get; set; } = null!;

    /// <summary>
    /// Gets or sets the assembly-qualified CLR type name of the integration event.
    /// </summary>
    public string EventType { get; set; } = null!;

    /// <summary>Gets or sets the JSON-serialized integration event payload.</summary>
    public string Payload { get; set; } = null!;

    /// <summary>Gets or sets the current lifecycle state of this inbox message.</summary>
    public InboxMessageStatus Status { get; set; }

    /// <summary>Gets or sets the number of failed handler invocation attempts.</summary>
    public int RetryCount { get; set; }

    /// <summary>Gets or sets the UTC time at which this message was received.</summary>
    public DateTimeOffset ReceivedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC time at which the handler completed successfully.
    /// <see langword="null"/> while pending or processing.
    /// </summary>
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC time until which this message is locked by the processor
    /// currently handling it. <see langword="null"/> when no lease is held.
    /// </summary>
    public DateTimeOffset? LockedUntilUtc { get; set; }

    /// <summary>
    /// Gets or sets the last error message recorded during a failed handler invocation.
    /// <see langword="null"/> when no error has occurred.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the correlation identifier propagated from the originating message chain.
    /// <see langword="null"/> when no correlation context was available on the inbound message.
    /// </summary>
    public CorrelationId? CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the causation identifier recording which message triggered this one.
    /// <see langword="null"/> for root events.
    /// </summary>
    public CausationId? CausationId { get; set; }
}
