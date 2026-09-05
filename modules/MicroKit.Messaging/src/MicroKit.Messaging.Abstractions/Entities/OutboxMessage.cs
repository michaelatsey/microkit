namespace MicroKit.Messaging;

/// <summary>
/// Represents a message stored in the transactional outbox, awaiting dispatch to
/// a broker or in-process consumer.
/// </summary>
/// <remarks>
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
    /// Gets or sets the nature of this message — what the dispatcher routes it to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The outbox is reentrant: one table, two natures, and a message may pass through the queue
    /// twice. Declared here rather than inferred from the payload's CLR type so that the routing
    /// decision is visible to SQL and does not require the payload-agnostic core to recognise a
    /// notification. Defaults to <see cref="MessageKind.Notification"/>, the zero value — which
    /// <see cref="MessageKind"/> pins with an explicit <c>= 0</c> rather than leaving to
    /// declaration order.
    /// </para>
    /// <para>
    /// <b>This column is what the dispatchers route on.</b> <c>TransportOutboxDispatcher</c>
    /// switches on it to build an envelope or to refuse the row, and <c>MediatROutboxDispatcher</c>
    /// switches on it to publish in process or to delegate inward without deserializing the payload
    /// at all. Neither performs a CLR type test, and a <see cref="MessageKind.Contract"/> row is
    /// delegated even when its payload happens to be an <c>INotification</c> — pinned by
    /// <c>DispatchAsync_WhenKindIsContract_AndPayloadIsANotification_StillDelegates</c>.
    /// </para>
    /// </remarks>
    public MessageKind MessageKind { get; set; }

    /// <summary>
    /// Gets or sets the stable wire identity of this message, e.g.
    /// <c>saasbtp.safety.constat-recorded.v1</c>. <see langword="null"/> for a
    /// <see cref="MessageKind.Notification"/>, which has no wire identity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The name a consumer knows this message by, and the one thing about it that must survive a
    /// CLR rename, an assembly split or a module extraction. <see cref="EventType"/> cannot serve:
    /// it is an assembly-qualified name the receiving process cannot resolve.
    /// </para>
    /// <para>
    /// Half of the replay natural key, with <see cref="OriginMessageId"/>. Nothing enforces that a
    /// <see cref="MessageKind.Contract"/> row actually carries one, and that is a <b>deferral, not
    /// an omission</b>: the integration-event publisher is the only writer that will ever set these
    /// columns, so the invariant belongs there — next to the code that knows the contract — rather
    /// than scattered across a schema anyone can write to.
    /// </para>
    /// <para>
    /// A database check constraint is the obvious alternative and is the wrong instrument. Its
    /// predicate has to spell <c>MessageKind &lt;&gt; 'Contract'</c>, hardcoding an enum member name
    /// into the schema: renaming a member would then break the constraint on top of the column, and
    /// the column at least fails at compile time where the constraint fails at run time.
    /// </para>
    /// <para>
    /// <b>The addressing key once a message leaves the process.</b> A
    /// <see cref="MessageKind.Contract"/> row is handed to <c>IMessageTransport</c> addressed by
    /// this name, and the receiving process resolves it to its <i>own</i> local CLR type through
    /// <c>IntegrationEventRegistry.TryResolveLocalType</c>. <see cref="EventType"/> remains the
    /// deserialization key on every path that stays in one process, and the two are not rivals —
    /// they serve disjoint sets of rows, because a <see cref="MessageKind.Notification"/> row has
    /// no contract name at all.
    /// </para>
    /// <para>
    /// <b>Non-null is a precondition of dispatch, enforced by the dispatcher.</b> Nullable here
    /// because a notification row legitimately has none; <c>TransportOutboxDispatcher</c> raises
    /// <see cref="OutboxPayloadException"/> on a contract row that reaches it without one, since
    /// such a row is unaddressable and no retry can change that.
    /// </para>
    /// </remarks>
    public string? ContractName { get; set; }

    /// <summary>
    /// Gets or sets the module that emitted this message, e.g. <c>/saasbtp/safety</c>.
    /// <see langword="null"/> for a <see cref="MessageKind.Notification"/>, which never leaves the
    /// process and so has no emitter to declare.
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
    /// Gets or sets the <see cref="Id"/> of the outbox row whose dispatch produced this one.
    /// <see langword="null"/> for a row that was not produced by a dispatch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Structural, not tracing — and that is why it is not called a causation.</b>
    /// <see cref="CorrelationId"/> and <see cref="CausationId"/> are diagnostic values rebuilt from
    /// strings, and both degrade to <see langword="null"/> when the string does not parse: losing a
    /// trace link is preferable to losing a message. This one cannot degrade. It is half of the
    /// unique key on (<see cref="OriginMessageId"/>, <see cref="ContractName"/>), so a silent null
    /// here silently switches deduplication off rather than merely blurring a trace.
    /// </para>
    /// <para>
    /// <b>Not <c>Source</c> either</b>, and that is not a matter of taste: <c>Source</c> already
    /// means <i>the emitting module</i> in this package — see <see cref="Source"/> and
    /// <c>IntegrationEventRegistration.Source</c>, and the log line that reads "as
    /// '{ContractName}' from '{Source}'". Both notions now live on this entity, which is why one
    /// of them had to take another word.
    /// </para>
    /// <para>
    /// <b>What it buys.</b> A redelivered dispatch re-runs its handlers, which publish the same
    /// contract again with the same origin row — so the second write collides on the unique index
    /// instead of producing a duplicate integration message. This closes the window a per-message
    /// settlement leaves open, because it also covers a crash occurring <i>after</i> the commit,
    /// which atomicity alone cannot.
    /// </para>
    /// <para>
    /// It follows that two handlers of one notification must not publish the same contract: they
    /// would collide on this key and the second would be absorbed as a duplicate. Forbidden by
    /// convention rather than detected.
    /// </para>
    /// <para>
    /// <b>Retention cannot outrun it, and that is enforced rather than assumed.</b> This key only
    /// rejects a duplicate while the row it produced is still in the table, so a
    /// <see cref="MessageKind.Contract"/> row must outlive every chance its origin has of being
    /// dispatched again. <c>IOutboxRetentionStore.DeleteProcessedAsync</c> therefore refuses to
    /// purge one while the row named here still exists and is not
    /// <see cref="OutboxMessageStatus.Published"/>.
    /// </para>
    /// <para>
    /// The window is a <i>state</i> and not a duration, which is why no retention default is the
    /// answer: automatic replay is bounded by <c>MaxRetries × MaxRetryBackoff</c>, both
    /// configurable, and an operator requeue of a dead-lettered origin is unbounded. Once the
    /// origin is <c>Published</c> nothing can re-dispatch it; once the origin has itself been
    /// purged there is nothing left to requeue.
    /// </para>
    /// <para>
    /// What that prevents is a duplicate <b>nothing downstream could recognise</b>: a republished
    /// contract is a new row with a new <see cref="Id"/> — the primary key forbids reusing the
    /// origin's — and a consumer deduplicates on that id, so it would run the handler a second time
    /// with full business side effects.
    /// </para>
    /// </remarks>
    public MessageId? OriginMessageId { get; set; }

    /// <summary>
    /// Gets or sets the assembly-qualified CLR type name of the serialized
    /// <see cref="Payload"/> — always its runtime type, never a declared or generic one.
    /// </summary>
    /// <remarks>
    /// <b>This is not necessarily an integration event, and on the domain-event path it is
    /// not one at all.</b> The outbox is payload-agnostic: <c>OutboxMessageFactory.CreateNotification</c>
    /// and <c>CreateContract</c> both accept <see cref="object"/> and stamp
    /// <c>payload.GetType().AssemblyQualifiedName</c>.
    /// What ends up here depends on who wrote the row:
    /// <list type="bullet">
    ///   <item>via <c>MicroKit.Messaging.MediatR</c> — a
    ///         <c>DomainEventNotification&lt;TEvent&gt;</c>, which is a MediatR
    ///         <c>INotification</c> and is <b>not</b> an <see cref="IIntegrationEvent"/>;</item>
    ///   <item>via a direct <see cref="IOutboxWriter"/> write — whatever that caller serialized,
    ///         typically an <see cref="IIntegrationEvent"/>.</item>
    /// </list>
    /// Do not wire a publisher that assumes one kind. <c>IOutboxDispatcher</c> is the seam that
    /// decides, and it routes on <see cref="MessageKind"/> rather than on the type named here:
    /// <c>MediatROutboxDispatcher</c> serves a <see cref="MessageKind.Notification"/> row and
    /// delegates the rest, and <c>TransportOutboxDispatcher</c> sends a
    /// <see cref="MessageKind.Contract"/> row without deserializing it at all.
    /// <para>
    /// <b>A local deserialization detail, not a contract.</b> An assembly-qualified name does not
    /// cross a service boundary — the consumer does not have the producer's assembly, so
    /// <c>Type.GetType</c> fails there; it works in process by accident. The identity a consumer
    /// addresses is <see cref="ContractName"/>, and the nature of the row is declared by
    /// <see cref="MessageKind"/> rather than recovered from the type named here.
    /// </para>
    /// <para>
    /// <b>Where the boundary falls, exactly.</b> This property remains the deserialization key on
    /// every path that stays in one process, and that is not a transitional state: a
    /// <see cref="MessageKind.Notification"/> row has no <see cref="ContractName"/> at all, so it
    /// can only ever be resolved from here. A <see cref="MessageKind.Contract"/> row handed to a
    /// transport resolves by <see cref="ContractName"/> instead, through
    /// <c>IntegrationEventRegistry</c>, because that is the only identity the receiving process can
    /// act on. The two keys are therefore not rivals — they serve disjoint sets of rows.
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
    /// <see cref="MessageEnvelope.OccurredOnUtc"/> carries this value.
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
    /// <see cref="MessageKind.Notification"/> row.
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
    /// degrades to <see langword="null"/> rather than failing when it cannot be parsed
    /// (<c>OutboxMessageFactory.ResolveCausation</c>). Do not confuse it with
    /// <see cref="OriginMessageId"/>, which records the same identity on a contract row for the
    /// opposite kind of reason — that one is half of a unique key and may never degrade.
    /// </para>
    /// </remarks>
    public CausationId? CausationId { get; set; }
}
