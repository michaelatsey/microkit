namespace MicroKit.Messaging.Abstractions.Outbox;

/// <summary>Dispatches a locked outbox message to its configured delivery target (MediatR or broker).</summary>
public interface IOutboxPublisher
{
    /// <summary>Publishes the given outbox message to its configured destination.</summary>
    /// <param name="message">The outbox message to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
