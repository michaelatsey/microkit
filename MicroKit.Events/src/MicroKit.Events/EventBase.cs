using MicroKit.Events.Contracts;

namespace MicroKit.Events;

/// <summary>Abstract base class for domain events. Provides default implementations for all <see cref="IEvent"/> members.</summary>
public abstract class EventBase : IEvent
{
    /// <inheritdoc />
    public Guid Id { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public string MessageType => GetType().FullName!;

    /// <inheritdoc />
    public DateTimeOffset TimestampUtc { get; } = DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; protected init; } = DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public string? CorrelationId { get; protected init; }

    /// <inheritdoc />
    public string? IdempotencyKey { get; protected init; }

    /// <inheritdoc />
    public string? RequestId { get; protected init; }

    /// <inheritdoc />
    public string? CausationId { get; protected init; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string>? Metadata { get; protected init; }
}
