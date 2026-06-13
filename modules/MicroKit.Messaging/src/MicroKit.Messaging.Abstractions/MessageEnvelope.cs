namespace MicroKit.Messaging;

/// <summary>
/// Wraps a strongly-typed integration event with routing and tracing metadata
/// for transmission through the messaging infrastructure.
/// </summary>
/// <typeparam name="T">The type of the integration event payload.
/// Must implement <see cref="IIntegrationEvent"/>.</typeparam>
/// <param name="Event">The integration event payload.</param>
/// <param name="MessageId">The unique identifier for this message.</param>
/// <param name="TenantId">The tenant identifier. Required — never <see langword="null"/> or empty.</param>
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
