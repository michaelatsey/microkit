namespace MicroKit.Messaging.Outbox;

using System.Text;
using MicroKit.Messaging.Serialization;

/// <summary>
/// The single production construction site for <see cref="OutboxMessage"/>, with one method per
/// nature of row.
/// </summary>
/// <remarks>
/// <para>
/// Two methods, because the two paths agree on almost nothing. A local message's identity and
/// occurrence time are given by its sender and its metadata comes from the scope; a contract's are
/// those the bus already put on the publication, and only a contract carries a contract name,
/// headers and a trace parent. Only the local one sets <see cref="OutboxMessage.Endpoint"/>: a
/// transport row is the column's null.
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
    /// Builds a local message's row (#188 D7): routed to <see cref="OutboxMessage.LocalEndpoint"/>,
    /// with no contract, its tenant, correlation and causation taken from the sender's scope.
    /// </summary>
    /// <param name="payload">The message; its runtime type is the deserialization key.</param>
    /// <param name="messageId">The message's identity — every consumer's inbox key.</param>
    /// <param name="occurredOnUtc">When the fact it announces happened.</param>
    /// <param name="source">The service's name.</param>
    /// <param name="context">The sender's execution context.</param>
    /// <returns>The unsaved row.</returns>
    public OutboxMessage CreateLocal(
        object payload,
        MessageId messageId,
        DateTimeOffset occurredOnUtc,
        string? source,
        IExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(messageId);
        ArgumentNullException.ThrowIfNull(context);

        return new OutboxMessage
        {
            Id = messageId,
            Endpoint = OutboxMessage.LocalEndpoint,
            Source = source,
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
    /// Creates a transport row — an integration event bound for a
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
    /// <returns>A pending transport row.</returns>
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
