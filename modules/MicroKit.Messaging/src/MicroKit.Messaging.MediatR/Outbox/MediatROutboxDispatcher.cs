namespace MicroKit.Messaging.MediatR.Outbox;

using System.Text;
using MicroKit.Messaging.Serialization;

/// <summary>
/// Routing <see cref="IOutboxDispatcher"/> decorator contributed by
/// <c>MicroKit.Messaging.MediatR</c>. It serves <see cref="MessageKind.Notification"/> rows by
/// publishing them in process through <see cref="IPublisher.Publish"/>, and delegates every other
/// row inward untouched.
/// </summary>
/// <remarks>
/// <para>
/// <b>It routes on <see cref="OutboxMessage.MessageKind"/>, not on the payload's CLR type.</b> The
/// column says what the row is; a type test only guesses. The previous implementation deserialized
/// every payload and branched on <c>is INotification</c>, which required assuming
/// <c>IIntegrationEvent</c> and <c>IDomainEventNotification</c> could never overlap — an assumption
/// nothing enforced, invisible to SQL, and wrong the moment a contract type happened to implement
/// both. Reading the column removes the assumption rather than documenting it.
/// </para>
/// <para>
/// <b>A <see cref="MessageKind.Contract"/> row is never deserialized here.</b> That is the whole
/// discipline of this class and it is easy to break by accident. Materializing a payload makes
/// dispatch depend on the producer's type graph — precisely what does not cross a process boundary —
/// and would make a message undispatchable whenever its CLR type moved assemblies between staging
/// and dispatch, though the bytes on the wire were perfectly well-formed. <c>TransportOutboxDispatcher</c>
/// was built to avoid exactly that; deserializing in front of it would reinstate the coupling while
/// leaving the inner class looking correct. Only the notification arm touches
/// <see cref="IMessageSerializer"/>.
/// </para>
/// <para>
/// <b>It knows nothing about the transport.</b> No <see cref="IMessageTransport"/>, no
/// <see cref="MessageEnvelope"/>, no <see cref="OutboxMessage.ContractName"/>, no
/// <see cref="OutboxMessage.Source"/>. Those belong to the inner dispatcher, and naming them here
/// would put one seam under two owners and pull the broker contract into this package's dependency
/// set.
/// </para>
/// <para>
/// <b>The inner dispatcher is optional.</b> A host that publishes only domain-event notifications
/// registers no transport dispatcher, which is a legal composition — see
/// <see cref="OutboxDispatcherKeys"/>. A null inner never returns as though it had delivered: the
/// only rows it cannot serve raise <see cref="OutboxConfigurationException"/>, which releases the
/// batch untouched, spends no retry budget and stops the worker.
/// </para>
/// <para>
/// <strong>Retry semantics:</strong> the whole <see cref="IPublisher.Publish"/> call is the retry
/// unit. There is no per-consumer inbox for notifications, so an outbox retry re-runs ALL
/// notification handlers for the message. Handlers on this path must be idempotent
/// (ADR-MSG-003 extended to the outbox→MediatR path by ADR-MSG-009).
/// </para>
/// <para>
/// The MediatR <see cref="INotification"/> / <see cref="IPublisher"/> usage here is the sole
/// permitted MediatR.Contracts reference in MicroKit.Messaging (ADR-MSG-009 carve-out).
/// </para>
/// </remarks>
internal sealed class MediatROutboxDispatcher(
    IOutboxDispatcher? inner,
    IMessageSerializer serializer,
    IPublisher publisher,
    ILogger<MediatROutboxDispatcher> logger)
    : IOutboxDispatcher
{
    /// <inheritdoc />
    /// <exception cref="OutboxPayloadException">
    /// A <see cref="MessageKind.Notification"/> row whose payload cannot be published: the
    /// <see cref="OutboxMessage.EventType"/> resolves to no CLR type, the
    /// <see cref="OutboxMessage.Payload"/> is not valid JSON for it, or the resolved type is not a
    /// MediatR <see cref="INotification"/>. All three are permanent — no retry rewrites a persisted
    /// row — so the processor dead-letters on first sight.
    /// </exception>
    /// <exception cref="OutboxConfigurationException">
    /// The row is not a notification and no inner dispatcher is registered. See the method remarks
    /// for why this is released rather than dead-lettered even when the kind is unrecognised.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>An unknown <see cref="MessageKind"/> is treated more conservatively here than by the inner
    /// dispatcher, and the difference is deliberate.</b> <c>TransportOutboxDispatcher</c> dead-letters
    /// one, correctly: it is a fully composed build, so a kind it cannot interpret is permanent for
    /// that deployment. With no inner registered, this build is by its own admission incomplete —
    /// the missing registration may be exactly the package that understands the kind — so the
    /// reversible verdict is the honest one. A row is not destroyed on the strength of a composition
    /// that was never finished. Where an inner <i>is</i> registered, the row is delegated and the
    /// inner's verdict stands unchanged.
    /// </para>
    /// </remarks>
    public async ValueTask DispatchAsync(OutboxMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.MessageKind is MessageKind.Notification)
        {
            await PublishAsync(message, ct).ConfigureAwait(false);
            return;
        }

        if (inner is null)
        {
            throw new OutboxConfigurationException(
                $"Outbox message {message.Id} carries MessageKind '{message.MessageKind}', which " +
                "this composition cannot route: MicroKit.Messaging.MediatR serves only " +
                $"{nameof(MessageKind.Notification)} rows, and no inner dispatcher is registered to " +
                "take the rest. Call AddTransportDispatcher() on the MessagingBuilder, or a broker " +
                "provider's Add{Provider}Transport() which calls it. The row is released untouched " +
                "and will dispatch once the missing registration is deployed.");
        }

        await inner.DispatchAsync(message, ct).ConfigureAwait(false);
    }

    private async ValueTask PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        // The serializer is type-based since #186, so the row's assembly-qualified EventType is
        // resolved here; the row keeps its payload as UTF-8 JSON text. A name that does not
        // resolve, a body that does not parse and a body denoting null are the same verdict:
        // this row can never be published.
        var eventType = RowEventTypes.Resolve(message.EventType)
            ?? throw new OutboxPayloadException(
                $"Cannot resolve EventType '{message.EventType}' of outbox message {message.Id} " +
                "in this process. The event type must be resolvable in the current assembly context.");

        object? payload;
        try
        {
            payload = serializer.Deserialize(Encoding.UTF8.GetBytes(message.Payload), eventType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new OutboxPayloadException(
                $"Cannot deserialize EventType '{message.EventType}' from outbox message " +
                $"{message.Id}: the payload is not valid JSON for it.",
                ex);
        }

        if (payload is null)
        {
            throw new OutboxPayloadException(
                $"Outbox message {message.Id} deserialized to null for EventType " +
                $"'{message.EventType}'.");
        }

        if (payload is not INotification notification)
        {
            throw new OutboxPayloadException(
                $"Outbox message {message.Id} is a {nameof(MessageKind.Notification)}, but its " +
                $"payload deserialized to '{payload.GetType().FullName}', which is not a MediatR " +
                "INotification and cannot be published. The MessageKind was set wrongly at staging, " +
                "or EventType names the wrong type.");
        }

        logger.LogDebug(
            "MediatROutboxDispatcher: publishing notification {MessageId} (EventType '{EventType}') via MediatR.",
            message.Id.Value,
            message.EventType);

        await publisher.Publish(notification, ct).ConfigureAwait(false);
    }
}
