namespace MicroKit.Messaging.Abstractions.Outbox;

public interface IOutboxMessageFetcher
{
    Task<IReadOnlyList<OutboxMessage>> FetchNextBatchAsync(
        string tenantId,
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken cancellationToken);
}
