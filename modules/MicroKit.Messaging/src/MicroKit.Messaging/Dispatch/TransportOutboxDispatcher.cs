namespace MicroKit.Messaging.Dispatch;

using System.Text;
using MicroKit.Messaging.Serialization;

/// <summary>
/// The standard <see cref="IOutboxDispatcher"/>: turns a <see cref="MessageKind.Contract"/> row
/// into a <see cref="MessageEnvelope"/> and hands it to <see cref="IMessageTransport"/>.
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
/// Registered as <b>scoped</b> with the transport, by <c>UseInMemoryTransport()</c>; the processor
/// resolves it from the per-message execution scope.
/// </para>
/// </remarks>
internal sealed class TransportOutboxDispatcher(
    IMessageTransport transport,
    ILogger<TransportOutboxDispatcher> logger)
    : IOutboxDispatcher
{
    /// <inheritdoc />
    /// <exception cref="OutboxConfigurationException">
    /// The row is a <see cref="MessageKind.Notification"/>. Nothing in this process can fan one
    /// out, because the package that does — <c>MicroKit.Messaging.MediatR</c> — is not registered.
    /// See the method remarks for why this is a configuration fault rather than a payload one.
    /// </exception>
    /// <exception cref="OutboxPayloadException">
    /// The row can never be dispatched without the persisted row itself changing: a
    /// <see cref="MessageKind.Contract"/> row missing its <see cref="OutboxMessage.ContractName"/>
    /// or <see cref="OutboxMessage.Source"/>, or a <see cref="OutboxMessage.MessageKind"/> this
    /// build cannot interpret at all.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Why a notification is a configuration fault and not a poison payload.</b> The payload is
    /// fine, the row is fine, and it would dispatch correctly the moment
    /// <c>AddMediatRDomainEvents()</c> is called — something staged a notification and nothing here
    /// can fan it out, which is the definition of a missing registration. The classification is not
    /// cosmetic: <see cref="OutboxPayloadException"/> dead-letters on <i>first sight</i>, so a host
    /// that forgot one line in its composition root would silently dead-letter every domain event
    /// in the system on the first poll, recoverable only by operator requeue.
    /// <see cref="OutboxConfigurationException"/> instead releases the entire batch untouched,
    /// consumes no retry budget, and stops the worker — the rows stay <c>Pending</c>, and deploying
    /// the missing package drains them. Loud and reversible beats loud and destructive.
    /// </para>
    /// <para>
    /// <b>Why an unknown kind is the opposite.</b> The asymmetry is deliberate.
    /// <see cref="MessageKind"/> is persisted as a string, so a row written by a later build — or
    /// edited by hand — can carry a value this build cannot interpret. A notification is a kind
    /// this build <i>understands</i> and cannot serve, fixable by deployment; an unknown kind
    /// cannot be interpreted at all, and re-reading the row will not change that. Permanent for
    /// this deployment, so it dead-letters.
    /// </para>
    /// </remarks>
    public async ValueTask DispatchAsync(OutboxMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        switch (message.MessageKind)
        {
            case MessageKind.Contract:
                await SendAsync(message, ct).ConfigureAwait(false);
                return;

            case MessageKind.Notification:
                throw new OutboxConfigurationException(
                    $"Outbox message {message.Id} is a {nameof(MessageKind.Notification)}, which " +
                    "this process has no way to dispatch: notifications are fanned out in-process " +
                    "by MicroKit.Messaging.MediatR, and it is not registered. Call " +
                    "AddMediatRDomainEvents() on the MessagingBuilder, or stop staging " +
                    $"notifications. EventType: '{message.EventType}'.");

            default:
                throw new OutboxPayloadException(
                    $"Outbox message {message.Id} carries MessageKind '{message.MessageKind}', " +
                    "which this build cannot interpret. The column is persisted as a string, so " +
                    "this row was most likely written by a later version of MicroKit.Messaging or " +
                    "edited by hand. No composition of this build can route it.");
        }
    }

    private async ValueTask SendAsync(OutboxMessage message, CancellationToken ct)
    {
        // The row permits null on both columns because a notification legitimately has neither.
        // A contract row that reaches here without them is unaddressable, and the entity enforces
        // no pairing invariant — it is an EF Core entity with no constructor to enforce one in, so
        // this is the last place the check can happen. Permanent: no retry rewrites a persisted row.
        if (string.IsNullOrWhiteSpace(message.ContractName))
        {
            throw new OutboxPayloadException(
                $"Outbox message {message.Id} is a {nameof(MessageKind.Contract)} but carries no " +
                "ContractName. A contract is addressed on the wire by that name and by nothing " +
                "else, so this row can never be sent. Whoever staged it must set it — see " +
                "IIntegrationEventPublisher.");
        }

        if (string.IsNullOrWhiteSpace(message.Source))
        {
            throw new OutboxPayloadException(
                $"Outbox message {message.Id} is a {nameof(MessageKind.Contract)} but carries no " +
                $"Source. The emitting module travels with every contract ('{message.ContractName}' " +
                "here) and is stamped at staging from the module's registered source. Whoever " +
                "staged this row did not set it.");
        }

        // The row keeps its string payload; the envelope carries bytes (#186 boundary). Payload is
        // the default serializer's JSON text, so it goes out as UTF-8 application/json. The
        // reserved headers come from the row's own columns, captured at staging — traceparent
        // included, which is the trace of the publication rather than of this relay.
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

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
