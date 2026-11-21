namespace MicroKit.Messaging.Dispatch;

using System.Text;
using MicroKit.Messaging.Serialization;

/// <summary>
/// The transport <see cref="IOutboxDispatcher"/>: turns a transport row (no
/// <see cref="OutboxMessage.Endpoint"/>) into a <see cref="MessageEnvelope"/> and hands it to
/// <see cref="IMessageTransport"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>It deserializes nothing, and the two dependencies it does not have are the design.</b> There
/// is no <c>IMessageSerializer</c> and no <c>IntegrationEventRegistry</c> here, and adding either
/// would be a regression rather than a convenience. The payload was serialized by this same process
/// at staging, so materializing it would resolve a type, build an object and re-serialize it to
/// identical bytes — while making the send path depend on the producer's type graph, which is
/// precisely what does not cross a process boundary. It would also make a message unsendable
/// whenever its CLR type moved assemblies between staging and dispatch, though the bytes on the
/// wire were perfectly well-formed. The receiver resolves <see cref="OutboxMessage.ContractName"/>
/// to its own local type; that is the whole point of a contract name.
/// </para>
/// <para>
/// The consequence is worth stating rather than discovering: this dispatcher <b>cannot detect a
/// malformed payload</b>, because detecting one means deserializing, and it deliberately does not.
/// A corrupt payload therefore travels and dead-letters at the consumer, in the consumer's inbox —
/// the correct place for it, since the receiver is the party that knows what the name should
/// deserialize into, but it means a producer-side operator can see a healthy queue during a
/// consumer-side incident.
/// </para>
/// <para>
/// <b><see cref="IMessageTransport"/> must stay a constructor dependency.</b> Resolving it lazily
/// from an <see cref="IServiceProvider"/> inside <see cref="DispatchAsync"/> would look harmless
/// and would silently change the failure mode of a missing registration: the container's
/// <see cref="InvalidOperationException"/> would then be thrown from inside a dispatch, where the
/// processor classifies it as <i>transient</i> — spending the full retry budget of every queued
/// message on a missing line in a composition root, then dead-lettering them all. Taken through the
/// constructor, the same missing registration fails while the processor is <i>activating</i> this
/// type, which it classifies batch-wide: the batch is released untouched, no retry is consumed, the
/// batch result reports <see cref="OutboxBatchAbortReason.DispatcherActivationFailed"/>, and the
/// worker backs off and tries again on the next cycle, logging each time. The rows stay
/// <see cref="OutboxMessageStatus.Pending"/> until a transport is there to take them.
/// </para>
/// <para>
/// <b>No <c>try</c>/<c>catch</c> around the send.</b> A transport's exception type <i>is</i> its
/// failure classification — permanent, transport-wide, or transient — and the processor routes on
/// it. Wrapping or re-typing anything here would destroy the only signal the engine has.
/// </para>
/// <para>
/// Registered as <b>scoped</b> with the transport, keyed <see cref="OutboxDispatchers.Transport"/>,
/// by <c>UseInMemoryTransport()</c> or a broker provider; the processor resolves it from the
/// per-message execution scope.
/// </para>
/// </remarks>
internal sealed class TransportOutboxDispatcher(
    IMessageTransport transport,
    ILogger<TransportOutboxDispatcher> logger)
    : IOutboxDispatcher
{
    /// <inheritdoc />
    /// <exception cref="OutboxPayloadException">
    /// The transport row carries no <see cref="OutboxMessage.ContractName"/> or no
    /// <see cref="OutboxMessage.Source"/>: unaddressable, and no retry rewrites a persisted row.
    /// </exception>
    /// <remarks>
    /// Reached only for a transport row: <c>OutboxProcessor</c> resolves the dispatcher keyed by the
    /// row's <see cref="OutboxMessage.Endpoint"/> (<see cref="OutboxDispatchers"/>), so a local row
    /// never gets here and this class needs no routing of its own.
    /// </remarks>
    public async ValueTask DispatchAsync(OutboxMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        await SendAsync(message, ct).ConfigureAwait(false);
    }

    private async ValueTask SendAsync(OutboxMessage message, CancellationToken ct)
    {
        // The row permits null on both columns because a local row legitimately has no contract.
        // A transport row that reaches here without them is unaddressable, and the entity enforces
        // no pairing invariant — it is an EF Core entity with no constructor to enforce one in, so
        // this is the last place the check can happen. Permanent: no retry rewrites a persisted row.
        if (string.IsNullOrWhiteSpace(message.ContractName))
        {
            throw new OutboxPayloadException(
                $"Outbox message {message.Id} is a transport row but carries no " +
                "ContractName. A contract is addressed on the wire by that name and by nothing " +
                "else, so this row can never be sent. Whoever staged it must set it; the bus " +
                "always does.");
        }

        if (string.IsNullOrWhiteSpace(message.Source))
        {
            throw new OutboxPayloadException(
                $"Outbox message {message.Id} is a transport row but carries no " +
                $"Source. The emitting module travels with every contract ('{message.ContractName}' " +
                "here) and is stamped at staging from the module's registered source. Whoever " +
                "staged this row did not set it.");
        }

        // The row keeps its string payload; the envelope carries bytes (#186 boundary). Payload is
        // the default serializer's JSON text, so it goes out as UTF-8 application/json. The
        // reserved headers come from the row's own columns, captured at staging — traceparent
        // included, which is the trace of the publication rather than of this relay.
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

        // The publication's own headers first, so a reserved one stored by hand cannot win over the
        // columns below.
        if (message.Headers is { } extra)
        {
            foreach (var (name, value) in extra)
            {
                if (!MessageHeaders.IsReserved(name))
                {
                    headers[name] = value;
                }
            }
        }

        // Non-nullable and required on a stored row, but a row built by hand can still carry null;
        // the dispatcher sends it without a correlation rather than failing it until dead-letter.
        if (message.CorrelationId is not null)
        {
            headers[MessageHeaders.CorrelationId] = message.CorrelationId.Value.ToString();
        }

        if (message.TenantId is not null)
        {
            headers[MessageHeaders.TenantId] = message.TenantId;
        }

        if (message.CausationId is not null)
        {
            headers[MessageHeaders.CausationId] = message.CausationId.Value.ToString();
        }

        if (message.TraceParent is not null)
        {
            headers[MessageHeaders.TraceParent] = message.TraceParent;
        }

        var envelope = new MessageEnvelope
        {
            Id = message.Id.Value.ToString(),
            Source = message.Source,
            Type = message.ContractName,
            Time = message.OccurredOnUtc,
            DataContentType = SystemTextJsonMessageSerializer.JsonContentType,
            Headers = headers,
            Body = Encoding.UTF8.GetBytes(message.Payload),
        };

        await transport.SendAsync(envelope, ct).ConfigureAwait(false);

        TransportDispatcherLogs.EnvelopeSent(
            logger, message.Id.Value, envelope.Type, envelope.Source);
    }
}
