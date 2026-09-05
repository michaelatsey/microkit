namespace MicroKit.Messaging.Outbox;

using System.Diagnostics;

/// <summary>
/// The single production construction site for <see cref="OutboxMessage"/>, with one method per
/// nature of row.
/// </summary>
/// <remarks>
/// <para>
/// Two methods rather than one with a <see cref="MessageKind"/> parameter: the two paths agree on
/// almost nothing. A notification's identity is supplied by the caller and a contract's is
/// generated; a notification's occurrence time is intrinsic and required while a contract's is
/// optional; and only a contract carries a contract name, a source, an origin and a trace parent.
/// One method with every parameter, half ignored per call, hides which are meaningful.
/// </para>
/// <para>
/// Both set <see cref="OutboxMessage.MessageKind"/> explicitly. <c>Notification</c> is the enum's
/// zero value, so leaving it unset would be correct by accident; with a sibling that sets
/// <c>Contract</c>, an omission would read as one.
/// </para>
/// <para>
/// Singleton-safe: <see cref="IExecutionContext"/> is a method parameter, never a constructor
/// dependency (ADR-MSG-008 §7). Injecting the scoped context into this singleton would capture the
/// first scope's values forever.
/// </para>
/// </remarks>
/// <param name="serializer">Serializes the payload. Always by runtime type, never by a generic
/// argument.</param>
/// <param name="timeProvider">
/// Supplies the staging time. Injected rather than read from <see cref="DateTimeOffset.UtcNow"/> so
/// that <c>CreatedAtUtc</c> — which the claim orders on — and the occurrence time substituted for an
/// unsupplied <c>occurredOnUtc</c> are both assertable against exact values in tests rather than
/// approximated with a tolerance.
/// </param>
public sealed class OutboxMessageFactory(IMessageSerializer serializer, TimeProvider timeProvider)
{
    /// <summary>
    /// Creates a <see cref="MessageKind.Notification"/> row — a domain event fanned out in process.
    /// </summary>
    /// <param name="payload">
    /// The object to serialize into <see cref="OutboxMessage.Payload"/>. Any runtime type is
    /// accepted; serialization uses <c>payload.GetType()</c>. In the domain-event flow this is
    /// typically an <c>IDomainEventNotification{TEvent}</c> (from <c>MicroKit.Messaging.MediatR</c>).
    /// </param>
    /// <param name="messageId">
    /// The stable, end-to-end identifier. Supplied by the caller from the domain event's intrinsic
    /// <c>EventId</c> so that the outbox row and the inbox dedup key share the same identity.
    /// </param>
    /// <param name="occurredOnUtc">
    /// The UTC time at which the domain event occurred. Sourced from the domain event's intrinsic
    /// <c>OccurredAt</c> property.
    /// </param>
    /// <param name="context">
    /// The ambient execution context supplying <c>TenantId</c>, <c>CorrelationId</c> and
    /// <c>CausationId</c>.
    /// </param>
    /// <returns>
    /// A pending <see cref="MessageKind.Notification"/> row. It carries no
    /// <see cref="OutboxMessage.ContractName"/>, <see cref="OutboxMessage.Source"/> or
    /// <see cref="OutboxMessage.OriginMessageId"/>: a notification never leaves the process, so it
    /// has no wire identity and no emitter to declare.
    /// </returns>
    public OutboxMessage CreateNotification(
        object payload,
        Guid messageId,
        DateTimeOffset occurredOnUtc,
        IExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(context);

        return new OutboxMessage
        {
            Id = MessageId.From(messageId),
            MessageKind = MessageKind.Notification,
            EventType = payload.GetType().AssemblyQualifiedName!,
            Payload = serializer.Serialize(payload),
            TenantId = context.TenantId,   // null pass-through — ADR-MSG-008 §5
            CorrelationId = ResolveCorrelation(context),
            CausationId = ResolveCausation(context),
            OccurredOnUtc = occurredOnUtc,
            CreatedAtUtc = timeProvider.GetUtcNow(),
            Status = OutboxMessageStatus.Pending,
            RetryCount = 0,
        };
    }

    /// <summary>
    /// Creates a <see cref="MessageKind.Contract"/> row — an integration event bound for a
    /// transport.
    /// </summary>
    /// <param name="payload">The integration event. Business payload only.</param>
    /// <param name="contractName">
    /// The wire contract name from <see cref="IntegrationEventAttribute"/>, e.g.
    /// <c>saasbtp.safety.constat-recorded.v1</c>.
    /// </param>
    /// <param name="source">The emitting module, e.g. <c>/saasbtp/safety</c>.</param>
    /// <param name="originMessageId">
    /// The outbox row whose dispatch produced this one, or <see langword="null"/> when publishing
    /// outside a dispatch. Half of the replay key; see <see cref="OutboxMessage.OriginMessageId"/>.
    /// </param>
    /// <param name="occurredOnUtc">
    /// When the underlying fact happened, if the caller knows.
    /// </param>
    /// <param name="context">
    /// The ambient execution context supplying <c>TenantId</c>, <c>CorrelationId</c> and
    /// <c>CausationId</c>.
    /// </param>
    /// <returns>A pending <see cref="MessageKind.Contract"/> row.</returns>
    /// <remarks>
    /// <para>
    /// <b><paramref name="occurredOnUtc"/> is resolved here, not deferred.</b>
    /// <see cref="OutboxMessage.OccurredOnUtc"/> and <see cref="MessageEnvelope.OccurredOnUtc"/> are
    /// both non-nullable, so "not stated" cannot survive onto the row: null becomes the staging
    /// time. That is the same fallback the transport would otherwise apply, moved one step earlier
    /// and made visible — the publisher logs when it happens, which is the only remaining trace of
    /// the distinction.
    /// </para>
    /// <para>
    /// <b>The trace parent is captured here and only for contracts.</b> This is the last moment the
    /// producing trace is current: the relay runs later, on another thread, under another activity.
    /// A notification row is fanned out in this same process and has no wire to carry a trace onto,
    /// so it is not captured there.
    /// </para>
    /// </remarks>
    public OutboxMessage CreateContract(
        object payload,
        string contractName,
        string source,
        MessageId? originMessageId,
        DateTimeOffset? occurredOnUtc,
        IExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(context);

        var createdAtUtc = timeProvider.GetUtcNow();

        return new OutboxMessage
        {
            Id = MessageId.New(),
            MessageKind = MessageKind.Contract,
            ContractName = contractName,
            Source = source,
            OriginMessageId = originMessageId,

            // The local deserialization key. It never travels — a receiving process cannot resolve
            // an assembly-qualified name — but the column is required and a row staged in a
            // modular monolith may still be read locally.
            EventType = payload.GetType().AssemblyQualifiedName!,
            Payload = serializer.Serialize(payload),

            TenantId = context.TenantId,   // null pass-through — ADR-MSG-008 §5
            CorrelationId = ResolveCorrelation(context),
            CausationId = ResolveCausation(context),
            TraceParent = Activity.Current?.Id,

            CreatedAtUtc = createdAtUtc,
            OccurredOnUtc = occurredOnUtc ?? createdAtUtc,

            Status = OutboxMessageStatus.Pending,
            RetryCount = 0,
        };
    }

    /// <summary>
    /// Rebuilds the correlation from the context's string form, substituting a fresh one when it
    /// does not parse.
    /// </summary>
    /// <remarks>
    /// <see cref="OutboxMessage.CorrelationId"/> is non-nullable and mapped <c>IsRequired</c>, so
    /// there is no null to degrade to. Losing a trace link is the right trade against losing the
    /// message, which is what writing null would cost.
    /// </remarks>
    private static CorrelationId ResolveCorrelation(IExecutionContext context)
        => Guid.TryParse(context.CorrelationId, out var correlation)
            ? CorrelationId.From(correlation)
            : CorrelationId.New();

    /// <summary>Rebuilds the causation, which legitimately has no value on a root event.</summary>
    private static CausationId? ResolveCausation(IExecutionContext context)
        => Guid.TryParse(context.CausationId, out var causation)
            ? CausationId.From(causation)
            : null;
}
