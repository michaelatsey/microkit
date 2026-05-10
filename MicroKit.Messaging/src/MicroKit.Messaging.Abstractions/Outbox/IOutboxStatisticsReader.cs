using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Messaging.Abstractions.Outbox;

public record OutboxStatistics(
    long PendingCount,
    long ProcessingCount,
    long PublishedCount,
    long FailedCount,
    DateTimeOffset? OldestPendingMessage);

public interface IOutboxStatisticsReader
{
    Task<OutboxStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}
