namespace MicroKit.Messaging.Spi;

/// <summary>
/// Represents a message stored in the transactional outbox, awaiting dispatch to
/// a broker or in-process consumer.
/// </summary>
/// <remarks>
/// <para>
/// An engine <b>row or claim type</b>, part of the SPI (<c>MicroKit.Messaging.Spi</c>). A store
/// exchanges these with the engine. Application code never constructs one, and reads one only from
/// operator tooling, through <see cref="IOutboxAdminStore.GetDeadLetteredAsync"/>.
/// </para>
/// <c>OutboxMessage</c> is a <c>sealed class</c> (not a record) because EF Core
/// change tracking requires mutable <c>{ get; set; }</c> properties.
/// <para>
/// <c>TenantId</c> is optional. <see langword="null"/> in single-tenant deployments.
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
    /// Optional. Null in single-tenant deployments.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// The <see cref="Endpoint"/> of a local message: delivered in this process, to the local
    /// consumers of its type, never handed to <see cref="IMessageTransport"/>.
    /// </summary>
    public const string LocalEndpoint = "local";

    /// <summary>
    /// Gets or sets where this message is dispatched: <see langword="null"/> for the transport,
    /// <see cref="LocalEndpoint"/> for the local endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One table carries both. A transport row is a contract the bus published: the store's outbox
    /// loop builds a <see cref="MessageEnvelope"/> from it and hands it to
    /// <see cref="IMessageTransport"/>. A local row is an in-process message — a domain-event
    /// notification of <c>MicroKit.Messaging.MediatR</c> — that the same loop delivers through the
    /// receive pipeline, once per local consumer, each guarded by its own inbox row (#188 D7). The
    /// row is <see cref="OutboxMessageStatus.Published"/> once every consumer's delivery is
    /// recorded.
    /// </para>
    /// <para>
    /// Routing reads this column, never the payload's CLR type. Any other value cannot be routed by
    /// this build: such a row is dead-lettered on first sight.
    /// </para>
    /// </remarks>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the stable wire identity of this message, e.g.
    /// <c>saasbtp.safety.constat-recorded.v1</c>. <see langword="null"/> for a local message
    /// (<see cref="LocalEndpoint"/>), which has no wire identity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The name a consumer knows this message by, and the one thing about it that must survive a
    /// CLR rename, an assembly split or a module extraction. <see cref="EventType"/> cannot serve:
    /// it is an assembly-qualified name the receiving process cannot resolve.
    /// </para>
    /// <para>
    /// Nothing in the schema enforces that a transport row carries one: the bus is the only writer
    /// that sets these columns, so the invariant lives there, next to the code that knows the
    /// contract.
    /// </para>
    /// <para>
    /// <b>The addressing key once a message leaves the process.</b> A transport row is handed to
    /// <c>IMessageTransport</c> addressed by this name, and the receiving process resolves it to its
    /// <i>own</i> local CLR type through <c>IntegrationEventRegistry.TryResolveLocalType</c>.
    /// <see cref="EventType"/> is the deserialization key of a local row, which has no contract name
    /// at all: the two serve disjoint sets of rows.
    /// </para>
    /// <para>
    /// <b>Non-null is a precondition of dispatch, enforced by the dispatcher.</b> A transport row
    /// that reaches it without one is unaddressable and dead-lettered: no retry can change it.
    /// </para>
    /// </remarks>
    public string? ContractName { get; set; }

    /// <summary>
    /// Gets or sets the module that emitted this message, e.g. <c>/saasbtp/safety</c>.
    /// For a local message (<see cref="LocalEndpoint"/>), the service's own name, when it has one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Identifies the <b>module</b>, not the deployment, and stays constant when that module is
    /// extracted into its own service — which is what makes the extraction a non-event for
    /// consumers. Recorded per contract at registration rather than per host, so several modules
    /// composed into one process each keep their own identity.
    /// </para>
    /// <para>
    /// <b>Stamped on the row at staging, never resolved at dispatch.</b> Reading it from
    /// <c>IntegrationEventRegistry</c> when the message is sent would look equivalent and is not:
    /// it would make the emitted source a function of the composition running <i>now</i> rather
    /// than of what was staged, so a row staged before a rename would travel under the new name.
    /// That is the defect ADR-MSG-018 removed by sourcing every field from the row, and it must not
    /// be reintroduced as a simplification.
    /// </para>
    /// </remarks>
    public string? Source { get; set; }

    /// <summary>
    /// Gets or sets the extra headers of the publication: those of <c>PublishOptions.Headers</c>
    /// and those the send middleware set. <see langword="null"/> when there are none.
    /// </summary>
    /// <remarks>
    /// Never holds a reserved header (<c>tenantid</c>, <c>correlationid</c>, <c>causationid</c>,
    /// <c>traceparent</c>): those are rebuilt at dispatch from the row's own columns, so a header
    /// here can never forge them. Copied onto the envelope verbatim.
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Gets or sets the assembly-qualified CLR type name of the serialized
    /// <see cref="Payload"/> — always its runtime type, never a declared or generic one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not necessarily an integration event.</b> A transport row carries one; a local row
    /// carries whatever was sent locally — a <c>DomainEventNotification&lt;TEvent&gt;</c> of
    /// <c>MicroKit.Messaging.MediatR</c>, which is not an <see cref="IIntegrationEvent"/>.
    /// </para>
    /// <para>
    /// <b>A local deserialization key, not a contract.</b> An assembly-qualified name does not cross
    /// a service boundary — the consumer does not have the producer's assembly. A transport row is
    /// sent without being deserialized and resolved on the far side by <see cref="ContractName"/>;
    /// a local row, which never leaves the process, is resolved from here.
    /// </para>
    /// </remarks>
    public string EventType { get; set; } = null!;

    /// <summary>
    /// Gets or sets the JSON-serialized payload, produced by <c>IMessageSerializer</c> from the
    /// type named in <see cref="EventType"/>. See that property for what it may hold.
    /// </summary>
    public string Payload { get; set; } = null!;

    /// <summary>Gets or sets the current lifecycle state of this message.</summary>
    public OutboxMessageStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the number of failed dispatch attempts.
    /// Incremented on each transient failure before the status resets to
    /// <see cref="OutboxMessageStatus.Pending"/>.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Gets or sets the UTC time at which the originating event occurred, supplied by the
    /// caller from the event's own timestamp rather than generated at write time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Intrinsic to the event, not to the row (ADR-MSG-008 §4) — unlike <see cref="CreatedAtUtc"/>.
    /// It is what a consumer orders and windows on, and it is the only timestamp that travels:
    /// <see cref="MessageEnvelope.Time"/> carries this value.
    /// </para>
    /// <para>
    /// <b>The claim no longer orders on it, and operator tooling still does.</b> Dispatch order is
    /// <see cref="CreatedAtUtc"/>; <c>GetDeadLetteredAsync</c> keeps this one, because triage wants
    /// the business fact rather than the queue position. The two are deliberately different and
    /// harmonising them would break one of the two purposes.
    /// </para>
    /// <para>
    /// Non-nullable, so "the caller did not say" cannot be represented here. The integration-event
    /// publisher resolves an unstated occurrence time to <see cref="CreatedAtUtc"/> at staging and
    /// logs that it did — an honest approximation rather than a silent one.
    /// </para>
    /// </remarks>
    public DateTimeOffset OccurredOnUtc { get; set; }

    /// <summary>Gets or sets the UTC time at which this outbox row was created.</summary>
    /// <remarks>
    /// <para>
    /// <b>The claim orders on this, and that is the outbox's dispatch order.</b> Ordering on
    /// <see cref="OccurredOnUtc"/> would let a backdated event jump the whole queue, and would make
    /// queue position depend on a value the caller supplies — which on the contract path is an
    /// optional parameter of a public method.
    /// </para>
    /// <para>
    /// Always set, and the writer's responsibility: nothing defaults it, so a row written straight
    /// through <see cref="IOutboxWriter"/> with the property omitted persists <c>0001-01-01</c> and
    /// then heads the queue permanently. <c>OutboxMessageFactory</c> stamps it from the injected
    /// <c>TimeProvider</c> on both paths.
    /// </para>
    /// </remarks>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the W3C <c>traceparent</c> current when this row was staged.
    /// <see langword="null"/> when there was no active activity, and on every
    /// local row (<see cref="LocalEndpoint"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Captured at staging because that is the last moment the producing trace is current: the
    /// relay runs later, on another thread, under another activity, so nothing downstream can
    /// reconstruct it. Without it a consumer's work appears as an unrelated trace, and the chain
    /// breaks exactly where crossing an asynchronous boundary makes it most valuable.
    /// </para>
    /// <para>
    /// <b>It does not travel yet.</b> <see cref="MessageEnvelope"/> declares no such member, and
    /// adding one is additive when a transport needs it — a member that could only ever be null is
    /// worse than an absent one, because a consumer builds on it. The column exists first because
    /// the value cannot be recovered later; the member cannot exist first, because there would be
    /// nothing to put in it.
    /// </para>
    /// <para>
    /// <b>And it is not yet restored on the reentrant hop either.</b> <c>OutboxProcessor</c> starts
    /// no <c>Activity</c> from this column before dispatching, so a contract published by a
    /// notification handler captures the worker's ambient activity — usually none — rather than the
    /// trace that produced the row it came from. The correlation chain survives that hop in a
    /// column and the W3C trace does not. Both halves belong to the same piece of work; the forward
    /// note sits on the envelope construction in <c>TransportOutboxDispatcher</c>, which is the line
    /// the first half lands on.
    /// </para>
    /// </remarks>
    public string? TraceParent { get; set; }

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
    /// Gets or sets the ownership token of the processor that currently holds the lease.
    /// <see langword="null"/> when no lease is held.
    /// </summary>
    /// <remarks>
    /// Written by <c>IOutboxProcessorStore.ClaimBatchAsync</c> and cleared by every terminal
    /// write. It is what makes a lease verifiable on release, not only on acquisition: every
    /// settlement filters on it, so a processor whose lease expired mid-dispatch matches zero
    /// rows instead of silently overwriting the processor that legitimately took its messages
    /// over. Without it a late writer clobbers a legitimate one — a lost update that only
    /// manifests under lease expiry, which is to say under load or after a stall.
    /// </remarks>
    public Guid? ClaimToken { get; set; }

    /// <summary>
    /// Gets or sets the earliest UTC time at which this message is eligible for
    /// re-dispatch after a transient failure. <see langword="null"/> for the initial attempt.
    /// </summary>
    /// <remarks>
    /// Back-off formula, computed by the processor rather than the store:
    /// <c>NextRetryAtUtc = now + Uniform(0, min(2^RetryCount seconds, MaxRetryBackoff))</c>.
    /// The exponential term is capped by <c>OutboxProcessorOptions.MaxRetryBackoff</c>
    /// (default 1 hour), and full jitter is then applied over the whole interval. The jitter is
    /// not decoration: without it, messages that fail together — which is what a broker outage
    /// produces — retry together across every processor instance.
    /// </remarks>
    public DateTimeOffset? NextRetryAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the last error message recorded during a failed dispatch attempt.
    /// <see langword="null"/> when no error has occurred.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this message has been permanently
    /// dead-lettered. Always <see langword="true"/> when <see cref="Status"/> is
    /// <see cref="OutboxMessageStatus.Failed"/>, and always set in the same write.
    /// </summary>
    /// <remarks>
    /// Reached two ways, and the retry count only explains one of them: when the incremented
    /// <see cref="RetryCount"/> reaches <c>MaxRetries</c>, or <b>on the first attempt</b> when
    /// the dispatcher raises <see cref="OutboxPayloadException"/> — a payload that cannot be
    /// dispatched without the persisted row itself changing. A dead-lettered row can therefore
    /// carry a <see cref="RetryCount"/> of zero.
    /// </remarks>
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
    /// <remarks>
    /// <para>
    /// <b>Where it comes from.</b> <c>OutboxProcessor</c> puts the <see cref="Id"/> of the row it is
    /// dispatching into the per-message <c>IExecutionContext</c>, and every row staged inside that
    /// scope — a contract published by a notification handler, a cascade notification — is built
    /// from it by <c>OutboxMessageFactory</c>. So the value here is the <b>parent's
    /// <see cref="Id"/></b>, one hop up, not a chain identifier shared down a branch: that is
    /// <see cref="CorrelationId"/>, which is copied through unchanged.
    /// </para>
    /// <para>
    /// <b>Null means root, and it is the common case.</b> A row staged from an HTTP request, a
    /// command handler or a job has no message above it. Only rows produced <i>by dispatching
    /// another row</i> carry a value.
    /// </para>
    /// <para>
    /// Diagnostic, never load-bearing: nothing keys, indexes, filters or orders on it, and it
    /// degrades to <see langword="null"/> rather than failing when it cannot be parsed.
    /// </para>
    /// </remarks>
    public CausationId? CausationId { get; set; }
}
