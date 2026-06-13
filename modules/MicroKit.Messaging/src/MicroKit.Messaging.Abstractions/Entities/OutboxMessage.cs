namespace MicroKit.Messaging;

/// <summary>
/// Represents a message stored in the transactional outbox, awaiting dispatch to
/// a broker or in-process consumer.
/// </summary>
/// <remarks>
/// <c>OutboxMessage</c> is a <c>sealed class</c> (not a record) because EF Core
/// change tracking requires mutable <c>{ get; set; }</c> properties.
/// <para>
/// <c>TenantId</c> is mandatory — never <see langword="null"/> or empty.
/// Background processors read tenant context from this field, never from
/// <c>IHttpContextAccessor</c>.
/// </para>
/// <para>
/// <c>CorrelationId</c> is non-nullable: every outbox message must be traceable
/// to a correlation chain. Set it to <see cref="CorrelationId.New()"/> when no
/// upstream correlation exists.
/// </para>
/// </remarks>
public sealed class OutboxMessage
{
    /// <summary>Gets or sets the unique identifier for this outbox message.</summary>
    public MessageId Id { get; set; } = null!;

    /// <summary>
    /// Gets or sets the identifier of the tenant this message belongs to.
    /// Required — never <see langword="null"/> or empty.
    /// </summary>
    public string TenantId { get; set; } = null!;

    /// <summary>
    /// Gets or sets the assembly-qualified CLR type name of the integration event
    /// (e.g. <c>"MyApp.Orders.OrderPlacedEvent, MyApp"</c>).
    /// </summary>
    public string EventType { get; set; } = null!;

    /// <summary>Gets or sets the JSON-serialized integration event payload.</summary>
    public string Payload { get; set; } = null!;

    /// <summary>Gets or sets the current lifecycle state of this message.</summary>
    public OutboxMessageStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the number of failed dispatch attempts.
    /// Incremented on each transient failure before the status resets to
    /// <see cref="OutboxMessageStatus.Pending"/>.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>Gets or sets the UTC time at which the domain event occurred.</summary>
    public DateTimeOffset OccurredOnUtc { get; set; }

    /// <summary>Gets or sets the UTC time at which this outbox row was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC time at which this message was successfully dispatched
    /// or permanently dead-lettered. <see langword="null"/> while pending or processing.
    /// </summary>
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC time until which this message is locked by the processor
    /// that currently holds the lease. <see langword="null"/> when no lease is held.
    /// </summary>
    public DateTimeOffset? LockedUntilUtc { get; set; }

    /// <summary>
    /// Gets or sets the earliest UTC time at which this message is eligible for
    /// re-dispatch after a transient failure. <see langword="null"/> for the initial attempt.
    /// Back-off formula: <c>2^RetryCount</c> seconds, capped at 3600 s.
    /// </summary>
    public DateTimeOffset? NextRetryAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the last error message recorded during a failed dispatch attempt.
    /// <see langword="null"/> when no error has occurred.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this message has been permanently
    /// dead-lettered after exceeding the maximum retry count.
    /// Always <see langword="true"/> when <see cref="Status"/> is
    /// <see cref="OutboxMessageStatus.Failed"/>.
    /// </summary>
    public bool DeadLettered { get; set; }

    /// <summary>
    /// Gets or sets the correlation identifier linking this message to a logical
    /// request chain. Non-nullable — use <see cref="CorrelationId.New()"/> when
    /// no upstream correlation context is available.
    /// </summary>
    public CorrelationId CorrelationId { get; set; } = null!;

    /// <summary>
    /// Gets or sets the causation identifier recording which message triggered this one.
    /// <see langword="null"/> for root events that have no causal parent.
    /// </summary>
    public CausationId? CausationId { get; set; }
}
