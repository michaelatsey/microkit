namespace MicroKit.Messaging.Abstractions.Inbox;

/// <summary>
/// Processes pending inbox entries for a given consumer, dispatching each message to its registered
/// <see cref="IInboxHandler{TMessage}"/> and updating the processing state.
/// </summary>
public interface IInboxProcessor
{
    /// <summary>Processes the next batch of pending inbox entries for the specified consumer.</summary>
    /// <param name="tenantId">The tenant identifier whose inbox to process.</param>
    /// <param name="consumerName">The name of the consumer group to process.</param>
    /// <param name="batchSize">Maximum number of entries to process in this batch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ProcessBatchAsync(
        string tenantId,
        string consumerName,
        int batchSize,
        CancellationToken cancellationToken = default);
}
