namespace MicroKit.Messaging.Dispatch;

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
/// constructor, the same missing registration fails while the processor is resolving this type,
/// where it is converted into <see cref="OutboxConfigurationException"/>: the batch is released
/// untouched, no retry is consumed, and the worker stops so the defect is visible.
/// </para>
/// <para>
/// <b>No <c>try</c>/<c>catch</c> around the send.</b> A transport's exception type <i>is</i> its
/// failure classification — permanent, transport-wide, or transient — and the processor routes on
/// it. Wrapping or re-typing anything here would destroy the only signal the engine has.
/// </para>
/// <para>
/// Registered as <b>scoped</b> by <c>MessagingBuilder.AddTransportDispatcher()</c>; the processor
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

        var envelope = new MessageEnvelope(
            // Every field from the row, and MessageId above all: the receiver deduplicates on it,
            // so it must be identical across every redelivery of this row. Deriving it from the
            // payload would only survive a retry by coincidence.
            MessageId: message.Id.Value,
            ContractName: message.ContractName,
            Source: message.Source,
            Payload: message.Payload,
            TenantId: message.TenantId,
            // Null-conditional on both: OutboxMessage enforces no invariant on CorrelationId, so a
            // row written by anything other than OutboxMessageFactory can carry null — the same
            // reason OutboxProcessor guards it when building the execution context.
            CorrelationId: message.CorrelationId?.Value,
            CausationId: message.CausationId?.Value,
            OccurredOnUtc: message.OccurredOnUtc);

        // FORWARD NOTE — the trace link, for whoever adds a member to MessageEnvelope.
        //
        // message.TraceParent is NOT copied here, because MessageEnvelope declares no member for
        // it: a member that could only ever be null is worse than an absent one, since a consumer
        // builds on it (see OutboxMessage.TraceParent). Adding one is additive and this is the
        // line it lands on.
        //
        // There is a second, smaller gap to close at the same time, and it is upstream of this
        // class: OutboxProcessor starts no Activity from message.TraceParent before dispatching, so
        // a contract published by a notification handler captures the WORKER's ambient activity —
        // usually none — rather than the trace that produced the row it came from. The correlation
        // chain survives that hop in a column; the W3C trace does not. Both halves are needed for
        // an end-to-end trace, and doing only this one produces an envelope carrying a traceparent
        // that names the relay instead of the producer, which is worse than carrying none.
        //
        // Deliberately deferred, not overlooked. Reviewed and recorded 2026-08-28.

        // Unguarded on purpose: the transport's exception type is its failure classification, and
        // the processor routes on it. See the class remarks.
        await transport.SendAsync(envelope, ct).ConfigureAwait(false);

        TransportDispatcherLogs.EnvelopeSent(
            logger, message.Id.Value, envelope.ContractName, envelope.Source);
    }
}
