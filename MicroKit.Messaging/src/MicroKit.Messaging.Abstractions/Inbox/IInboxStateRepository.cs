using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Messaging.Abstractions.Inbox;

public interface IInboxStateRepository
{
    Task<IReadOnlyList<InboxState>> LockNextBatchAsync(
    string tenantId,
    string consumerName,
    int batchSize,
    TimeSpan lockDuration,
    CancellationToken cancellationToken = default);
    Task MarkConsumedAsync(
        IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        string id,
        string error,
        DateTimeOffset? nextRetryAt,
        CancellationToken cancellationToken = default);

    Task<int> GetBacklogCountAsync(
        string consumerName,
        CancellationToken cancellationToken = default);
}
