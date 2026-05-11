using MicroKit.Events.Contracts;

namespace MicroKit.Events;

/// <summary>Abstract base class for integration events published across service boundaries.</summary>
public abstract class IntegrationEventBase : IIntegrationEvent
{
    /// <inheritdoc />
    public Guid Id { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; protected init; } = DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public string MessageType => GetType().FullName!;

    /// <inheritdoc />
    public string? CorrelationId { get; protected init; }
}
