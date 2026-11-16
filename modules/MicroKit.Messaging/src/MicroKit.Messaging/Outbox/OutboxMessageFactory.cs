namespace MicroKit.Messaging.Outbox;

using System.Text;
using MicroKit.Messaging.Serialization;

/// <summary>
/// The single production construction site for <see cref="OutboxMessage"/>, with one method per
/// nature of row.
/// </summary>
/// <remarks>
/// <para>
/// Two methods rather than one with a <see cref="MessageKind"/> parameter: the two paths agree on
/// almost nothing. A notification's identity and occurrence time are intrinsic to its domain event
/// and its metadata comes from the scope; a contract's are those the bus already put on the
/// publication, and only a contract carries a contract name, a source, headers and a trace parent.
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
/// that <c>CreatedAtUtc</c> — which the claim orders on — is assertable against exact values in
/// tests rather than approximated with a tolerance.
/// </param>
internal sealed class OutboxMessageFactory(IMessageSerializer serializer, TimeProvider timeProvider)
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
    /// <see cref="OutboxMessage.ContractName"/> or <see cref="OutboxMessage.Source"/>: a
    /// notification never leaves the process, so it
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
            Payload = SerializeAsText(payload),
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
    /// transport — from a publication the bus has already addressed and headed.
    /// </summary>
    /// <param name="payload">The integration event. Business payload only.</param>
    /// <param name="messageId">The publication's identifier, the one the caller is returned.</param>
    /// <param name="contractName">The wire type, e.g. <c>saasbtp.safety.constat-recorded.v1</c>.</param>
    /// <param name="source">The emitting service.</param>
    /// <param name="occurredOnUtc">The publication instant, the envelope's <c>Time</c>.</param>
    /// <param name="headers">
    /// The envelope headers the bus built: the reserved ones become the row's columns, the others
    /// its <see cref="OutboxMessage.Headers"/>.
    /// </param>
    /// <returns>A pending <see cref="MessageKind.Contract"/> row.</returns>
    /// <remarks>
    /// The reserved headers are read back from what the bus wrote, never from the scope again, so
    /// the row carries exactly what a direct send would have put on the envelope — the trace parent
    /// included, captured while the producing trace is current.
    /// </remarks>
    public OutboxMessage CreateContract(
        object payload,
        MessageId messageId,
        string contractName,
        string source,
        DateTimeOffset occurredOnUtc,
        IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(headers);

        var extra = headers
            .Where(h => !MessageHeaders.IsReserved(h.Key))
            .ToDictionary(h => h.Key, h => h.Value, StringComparer.Ordinal);

        return new OutboxMessage
        {
            Id = messageId,
            MessageKind = MessageKind.Contract,
            ContractName = contractName,
            Source = source,

            // The local deserialization key. It never travels — a receiving process cannot resolve
            // an assembly-qualified name — but the column is required.
            EventType = payload.GetType().AssemblyQualifiedName!,
            Payload = SerializeAsText(payload),
            TenantId = headers.GetValueOrDefault(MessageHeaders.TenantId),
            CorrelationId = ResolveCorrelation(headers.GetValueOrDefault(MessageHeaders.CorrelationId)),
            CausationId = ResolveCausation(headers.GetValueOrDefault(MessageHeaders.CausationId)),
            TraceParent = headers.GetValueOrDefault(MessageHeaders.TraceParent),
            Headers = extra.Count == 0 ? null : extra,
            CreatedAtUtc = timeProvider.GetUtcNow(),
            OccurredOnUtc = occurredOnUtc,
            Status = OutboxMessageStatus.Pending,
            RetryCount = 0,
        };
    }

    private string SerializeAsText(object payload)
    {
        if (!SystemTextJsonMessageSerializer.IsJson(serializer.ContentType))
        {
            throw new OutboxConfigurationException(
                $"The registered {nameof(IMessageSerializer)} produces '{serializer.ContentType}', " +
                "but outbox rows store the body as application/json text. A serializer of another " +
                "content type works on the bus only; the outbox path requires a JSON serializer.");
        }

        return Encoding.UTF8.GetString(serializer.Serialize(payload, payload.GetType()).Span);
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
        => ResolveCorrelation(context.CorrelationId);

    private static CorrelationId ResolveCorrelation(string? correlationId)
        => Guid.TryParse(correlationId, out var correlation)
            ? CorrelationId.From(correlation)
            : CorrelationId.New();

    /// <summary>Rebuilds the causation, which legitimately has no value on a root event.</summary>
    private static CausationId? ResolveCausation(IExecutionContext context)
        => ResolveCausation(context.CausationId);

    private static CausationId? ResolveCausation(string? causationId)
        => Guid.TryParse(causationId, out var causation)
            ? CausationId.From(causation)
            : null;
}
