namespace MicroKit.Messaging;

/// <summary>One integration event, staged in its own queue and awaiting delivery.</summary>
/// <remarks>
/// <para>
/// <b>A table of its own, not a discriminated slice of the outbox.</b> One table behind a kind
/// column would be fewer moving parts, and that was the first instinct. It lost to a specific
/// risk: <see cref="OutboxMessage"/> carries domain event notifications and is drained by a
/// processor whose dispatcher performs a MediatR fan-out. Behind a discriminator, a single
/// misregistration would feed integration events into that fan-out, with a <c>WHERE</c> clause as
/// the only thing preventing it. Separate tables make the mistake <i>inexpressible</i> rather than
/// merely detectable — and that exact confusion has already cost this codebase once, when the
/// outbox's own documentation described it as carrying integration events and two independent
/// readers wired a publisher straight onto <see cref="IOutboxWriter"/>.
/// </para>
/// <para>
/// <b>That argument has since been revisited, and this table is being retired onto outbox rows.</b>
/// The paragraph above is kept because its reasoning still holds against what it was aimed at — a
/// discriminator <i>inferred</i> from the payload's CLR type. <see cref="OutboxMessage.MessageKind"/>
/// is not that: the nature of a row is <i>declared</i> by whoever wrote it and read back from a
/// column, so the misregistration this feared is no longer expressible either, and it is visible to
/// SQL besides. Both models are live in the interval — see
/// <see cref="OutboxMessage.ContractName"/>.
/// </para>
/// <para>
/// The two also diverge operationally: different transports, different dead-letter audiences, and
/// different retention windows — a notification may be purged once handled, an integration event
/// must outlive the longest plausible redelivery of any consumer.
/// </para>
/// <para>
/// <b>The durable form, not the wire form.</b> Metadata lives in columns so a claim query can
/// filter and order without parsing JSON; only <see cref="Data"/> is opaque. The envelope a broker
/// receives is assembled by the transport at delivery. Storing the wire format directly would put
/// the claim path behind a JSON parse and freeze one wire format into the schema.
/// </para>
/// <para>
/// A <c>sealed class</c> rather than a record: EF Core change tracking requires settable
/// properties.
/// </para>
/// <para>
/// <b>The delivery half is declared but not yet driven.</b> The relay that claims, leases, retries
/// and dead-letters these rows is a later lot. Staging writes identity, contract, payload, context
/// and timestamps; every other property below is the schema that relay will need, present now so
/// the table does not have to change under it.
/// </para>
/// </remarks>
public sealed class IntegrationEventMessage
{
    /// <summary>Gets or sets the message identifier — the delivery identity of this event.</summary>
    /// <remarks>
    /// Assigned at staging, and the only identity that exists. A consumer's inbox deduplicates on
    /// it, which is why it is returned to the caller: it is the one value linking a business
    /// operation to what a consumer eventually receives.
    /// </remarks>
    public MessageId Id { get; set; } = null!;

    /// <summary>
    /// Gets or sets the contract name, e.g. <c>saasbtp.safety.constat-recorded.v1</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="OutboxMessage.ContractName"/> carries the same notion on the outbox row and is
    /// the model that supersedes this one. The two are mapped to the same width deliberately; both
    /// are live until the publisher moves onto outbox rows.
    /// </remarks>
    public string ContractName { get; set; } = null!;

    /// <summary>Gets or sets the emitting module, e.g. <c>/saasbtp/safety</c>.</summary>
    /// <remarks>
    /// Identifies the module, not the deployment, and stays constant when that module is extracted
    /// into its own service — which is what makes the extraction a non-event for consumers.
    /// Recorded per contract at registration rather than per host, so several modules composed
    /// into one process each keep their own.
    /// </remarks>
    public string Source { get; set; } = null!;

    /// <summary>Gets or sets the JSON-serialized payload.</summary>
    public string Data { get; set; } = null!;

    /// <summary>
    /// Gets or sets the tenant this event belongs to. Null in single-tenant deployments.
    /// </summary>
    /// <remarks>
    /// Read from the ambient <c>IExecutionContext</c> at staging and carried on the row, so a
    /// background relay never has to reconstruct tenant context from an ambient it does not have.
    /// Optional for the same reason the outbox and inbox columns are: messaging must run without
    /// Tenancy (ADR-EXEC-001), and a single-tenant deployment legitimately has null on every row.
    /// </remarks>
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the correlation identifier linking this event to a request chain.
    /// </summary>
    public CorrelationId? CorrelationId { get; set; }

    /// <summary>Gets or sets the identifier of the message that caused this one.</summary>
    public CausationId? CausationId { get; set; }

    /// <summary>Gets or sets the W3C <c>traceparent</c> captured at staging.</summary>
    /// <remarks>
    /// Captured here because this is the last moment the producing trace is current: the relay
    /// runs later, on another thread, under another activity. Without it the consumer's work
    /// appears as an unrelated trace, and the chain breaks exactly where crossing an asynchronous
    /// boundary makes it most valuable.
    /// </remarks>
    public string? TraceParent { get; set; }

    /// <summary>Gets or sets when this row was staged. Always set.</summary>
    /// <remarks>
    /// <b>A claim orders on this, never on <see cref="OccurredOnUtc"/>.</b> Ordering on the
    /// business timestamp would let a backdated event jump the whole queue, and would make queue
    /// order depend on data a caller supplies.
    /// </remarks>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets when the underlying fact occurred. Null when the caller did not say.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="CreatedAtUtc"/> on purpose. The fact happened in the business
    /// transaction; this row is staged one relay later — minutes under load, hours after an
    /// incident. Collapsing the two would publish a wire timestamp that is really the relay's
    /// clock, and any consumer ordering or windowing on it would read the wrong thing.
    /// </para>
    /// <para>
    /// A notification handler has this value on the domain event and should pass it. Null means
    /// "not stated", and the transport falls back to <see cref="CreatedAtUtc"/> — an honest
    /// approximation rather than a silent one.
    /// </para>
    /// </remarks>
    public DateTimeOffset? OccurredOnUtc { get; set; }

    /// <summary>Gets or sets where this message currently is in the pipeline.</summary>
    public IntegrationEventStatus Status { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether delivery was permanently abandoned at least once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Orthogonal to <see cref="Status"/>, not redundant with it.</b> The status answers "where
    /// is this message now" and changes on every transition; this flag answers "was it ever given
    /// up on" and is monotone until an operator clears it. Folding both into a single
    /// <c>Failed</c> status would lose the second answer the moment a requeue moved the row back
    /// to <see cref="IntegrationEventStatus.Pending"/> — and the eligibility predicate would then
    /// have to infer abandonment from a value that changes constantly.
    /// </para>
    /// <para>
    /// A dead-lettered row therefore reads <c>Pending</c> with this flag set: pending delivery,
    /// abandoned. Requeue clears the flag and resets the retry count; nothing else changes.
    /// </para>
    /// </remarks>
    public bool DeadLettered { get; set; }

    /// <summary>Gets or sets the number of failed delivery attempts.</summary>
    public int RetryCount { get; set; }

    /// <summary>Gets or sets the earliest UTC time at which delivery may be retried.</summary>
    public DateTimeOffset? NextRetryAtUtc { get; set; }

    /// <summary>Gets or sets the UTC time until which a relay holds the lease.</summary>
    public DateTimeOffset? LockedUntilUtc { get; set; }

    /// <summary>
    /// Gets or sets the ownership token of the relay currently holding the lease.
    /// </summary>
    /// <remarks>
    /// Mapped as an EF concurrency token. Without that, the <c>UPDATE</c> a settlement emits
    /// carries only the primary key, and a relay whose lease expired mid-delivery overwrites the
    /// relay that legitimately took the message over. Consumed by the relay; declared and mapped
    /// here because the column belongs to the row, and because leaving it to a later lot means the
    /// day someone forgets, no test that does not exercise concurrency will notice.
    /// </remarks>
    public Guid? ClaimToken { get; set; }

    /// <summary>Gets or sets the UTC time at which delivery succeeded or was abandoned.</summary>
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    /// <summary>Gets or sets the last error recorded during a failed delivery.</summary>
    public string? ErrorMessage { get; set; }
}
