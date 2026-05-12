namespace MicroKit.Messaging.Abstractions.Inbox;

/// <summary>Fetches and locks the next batch of inbox state records for a consumer to process.</summary>
public interface IInboxStateFetcher
{
    /// <summary>Atomically locks and returns the next batch of pending state entries for the given consumer.</summary>
    /// <param name="tenantId">The tenant identifier whose inbox to query.</param>
    /// <param name="consumerName">The consumer group name.</param>
    /// <param name="batchSize">Maximum number of entries to return.</param>
    /// <param name="lockDuration">Duration for which each returned entry is locked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of <see cref="InboxState"/> entries locked for processing.</returns>
    Task<IReadOnlyList<InboxState>> FetchNextBatchAsync(
        string tenantId,
        string consumerName,
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default);
}
