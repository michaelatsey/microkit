namespace MicroKit.Messaging.Abstractions.Inbox;

public interface IInboxStateFetcher
{
    Task<IReadOnlyList<InboxState>> FetchNextBatchAsync(
        string tenantId,
        string consumerName,
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default);
}
