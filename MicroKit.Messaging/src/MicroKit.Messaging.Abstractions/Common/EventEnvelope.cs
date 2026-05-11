namespace MicroKit.Messaging.Abstractions.Common;

/// <summary>Non-generic base for all event envelopes, enabling type-safe access without generics.</summary>
public abstract class EventEnvelopeBase
{
    /// <summary>Gets the unique identifier of the event.</summary>
    public required string EventId { get; init; }

    /// <summary>Gets the tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the fully-qualified CLR type name of the event payload.</summary>
    public required string MessageType { get; init; }

    /// <summary>Gets the UTC timestamp when the event occurred.</summary>
    public DateTimeOffset OccurredOnUtc { get; init; }

    /// <summary>Gets the UTC timestamp when the event was published to the outbox.</summary>
    public DateTimeOffset PublishedAtUtc { get; init; }

    /// <summary>Gets an optional correlation identifier for distributed tracing.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Gets an optional causation identifier linking this event to its cause.</summary>
    public string? CausationId { get; init; }

    /// <summary>Gets an optional key used to enforce exactly-once processing.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Gets optional key-value metadata attached to the envelope.</summary>
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>Returns the raw payload as an <see cref="object"/>.</summary>
    public abstract object GetPayload();
}

/// <summary>Typed envelope that wraps an event payload for reliable outbox delivery.</summary>
/// <typeparam name="T">The type of the event payload.</typeparam>
public sealed class EventEnvelope<T> : EventEnvelopeBase
{
    /// <summary>Gets the strongly-typed event payload.</summary>
    public required T Payload { get; init; }

    /// <inheritdoc />
    public override object GetPayload() => Payload!;
}
