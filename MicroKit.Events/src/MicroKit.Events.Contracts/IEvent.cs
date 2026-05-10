namespace MicroKit.Events.Contracts;

/// <summary>
/// Exposes the domain event interface.
/// </summary>
public interface IEvent
{
    Guid Id { get; }
    string MessageType { get; }
    /// <summary>
    /// Moment où le message est créé/enregistré/persisté
    /// </summary>
    /// <value>
    /// The timestamp UTC.
    /// </value>
    DateTimeOffset TimestampUtc { get; }
    DateTimeOffset OccurredOnUtc { get; }
    string? CorrelationId { get; }
    string? IdempotencyKey { get; }
    string? RequestId { get; }
    string? CausationId { get; } // Exemple CommandId
    Dictionary<string, string>? Metadata { get; }
}
