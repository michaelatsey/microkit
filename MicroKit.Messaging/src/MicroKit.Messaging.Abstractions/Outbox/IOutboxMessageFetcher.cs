namespace MicroKit.Messaging.Abstractions.Outbox;

/// <summary>Fetches and locks the next batch of outbox messages for a tenant.</summary>
public interface IOutboxMessageFetcher
{
    /// <summary>Atomically locks and returns the next batch of pending outbox messages for the given tenant.</summary>
    /// <param name="tenantId">The tenant identifier whose outbox to query.</param>
    /// <param name="batchSize">Maximum number of messages to return.</param>
    /// <param name="lockDuration">Duration for which each returned message is locked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of <see cref="OutboxMessage"/> entries locked for dispatch.</returns>
    Task<IReadOnlyList<OutboxMessage>> FetchNextBatchAsync(
        string tenantId,
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken cancellationToken);
}
