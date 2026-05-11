namespace MicroKit.Events.Contracts;

/// <summary>Contract for integration events published across service boundaries.</summary>
public interface IIntegrationEvent : IEventBase
{
    /// <summary>Gets the fully-qualified CLR type name of this event, used for routing and deserialization.</summary>
    string MessageType { get; }

    /// <summary>Gets an optional identifier that correlates this event to a logical operation or request chain.</summary>
    string? CorrelationId { get; }
}
