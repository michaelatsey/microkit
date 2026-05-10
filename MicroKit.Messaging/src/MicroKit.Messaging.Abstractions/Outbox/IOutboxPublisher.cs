namespace MicroKit.Messaging.Abstractions.Outbox;

//public record OutboxContext(
//    string MessageId,
//    string MessageType,
//    string Payload,
//    OutboxDestination Destination
//    //string? CorrelationId = null,
//    //Dictionary<string, string>? Metadata = null
//    );

public interface IOutboxPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
