using System.Reflection.PortableExecutable;

namespace MicroKit.Messaging.Abstractions.Inbox;


public record InboxContext(
    string MessageId,
    string MessageType,
    string Payload,
    Dictionary<string, string> Headers)
{
    // Accès rapide aux clés standards
    public string? CorrelationId => Headers.GetValueOrDefault("CorrelationId");
    public string? IdempotencyKey => Headers.GetValueOrDefault("IdempotencyKey");
}

public interface IInboxPublisher
{
    Task PublishAsync(InboxContext context, CancellationToken cancellationToken = default);
}
