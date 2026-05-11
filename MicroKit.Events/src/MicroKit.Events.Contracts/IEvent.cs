namespace MicroKit.Events.Contracts;

/// <summary>Contract for domain events raised within a bounded context.</summary>
public interface IEvent : IEventBase
{
    /// <summary>Gets the fully-qualified CLR type name of this event, used for deserialization and routing.</summary>
    string MessageType { get; }

    /// <summary>Gets the UTC timestamp when the event was created or persisted.</summary>
    DateTimeOffset TimestampUtc { get; }

    /// <summary>Gets an optional identifier that correlates this event to a logical operation or request chain.</summary>
    string? CorrelationId { get; }

    /// <summary>Gets an optional key used to enforce exactly-once processing.</summary>
    string? IdempotencyKey { get; }

    /// <summary>Gets an optional identifier of the originating request.</summary>
    string? RequestId { get; }

    /// <summary>Gets an optional identifier of the command or event that caused this event (e.g. a CommandId).</summary>
    string? CausationId { get; }

    /// <summary>Gets optional key-value metadata attached to this event.</summary>
    IReadOnlyDictionary<string, string>? Metadata { get; }
}
