namespace MicroKit.Messaging;

/// <summary>
/// Wraps a strongly-typed integration event with routing and tracing metadata.
/// </summary>
/// <remarks>
/// <b>Nothing in v1 uses this type.</b> No package in MicroKit.Messaging constructs, consumes or
/// transmits a <see cref="MessageEnvelope{T}"/>: the outbox and inbox carry their metadata as
/// columns on <see cref="OutboxMessage"/> and <see cref="InboxMessage"/>, and
/// <c>IMessageSerializer</c> serializes the payload directly. It is reserved for a broker
/// transport that needs an on-the-wire envelope. Do not build against it expecting the pipeline
/// to populate or honour it.
/// </remarks>
/// <typeparam name="T">The type of the integration event payload.
/// Must implement <see cref="IIntegrationEvent"/>.</typeparam>
/// <param name="Event">The integration event payload.</param>
/// <param name="MessageId">The unique identifier for this message.</param>
/// <param name="TenantId">The tenant identifier. See <see cref="IIntegrationEvent.TenantId"/> —
/// non-null by declaration, emptiness unvalidated, and absent in single-tenant deployments.</param>
/// <param name="OccurredOnUtc">The UTC time at which the event occurred.</param>
/// <param name="CorrelationId">The correlation identifier, or <see langword="null"/> when no upstream chain exists.</param>
/// <param name="CausationId">The causation identifier, or <see langword="null"/> for root events.</param>
public sealed record MessageEnvelope<T>(
    T Event,
    MessageId MessageId,
    string TenantId,
    DateTimeOffset OccurredOnUtc,
    CorrelationId? CorrelationId = null,
    CausationId? CausationId = null)
    where T : IIntegrationEvent;
