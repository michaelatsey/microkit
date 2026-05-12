using System.Reflection.PortableExecutable;

namespace MicroKit.Messaging.Abstractions.Inbox;

/// <summary>
/// Carries the raw transport data needed to route an incoming message to its handler.
/// Standard headers (correlation, idempotency key) are accessible via convenience properties.
/// </summary>
/// <param name="MessageId">The unique identifier of the incoming message.</param>
/// <param name="MessageType">The fully-qualified CLR type name of the message payload.</param>
/// <param name="Payload">The serialized JSON payload.</param>
/// <param name="Headers">Transport-level headers attached to the message.</param>
public record InboxContext(
    string MessageId,
    string MessageType,
    string Payload,
    Dictionary<string, string> Headers)
{
    /// <summary>Gets the correlation identifier from the message headers, if present.</summary>
    public string? CorrelationId => Headers.GetValueOrDefault("CorrelationId");

    /// <summary>Gets the idempotency key from the message headers, if present.</summary>
    public string? IdempotencyKey => Headers.GetValueOrDefault("IdempotencyKey");
}

/// <summary>Dispatches a received transport message into the inbox pipeline for processing.</summary>
public interface IInboxPublisher
{
    /// <summary>Routes the incoming message context to the appropriate inbox handler.</summary>
    /// <param name="context">The raw transport context of the incoming message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishAsync(InboxContext context, CancellationToken cancellationToken = default);
}
