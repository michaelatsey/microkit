using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Messaging.Abstractions.Outbox;

/// <summary>A snapshot of outbox queue depths across all message statuses.</summary>
/// <param name="PendingCount">Number of messages awaiting dispatch.</param>
/// <param name="ProcessingCount">Number of messages currently locked and being processed.</param>
/// <param name="PublishedCount">Number of messages successfully published.</param>
/// <param name="FailedCount">Number of messages that permanently failed after all retries.</param>
/// <param name="OldestPendingMessage">Timestamp of the oldest pending message, or <see langword="null"/> if none.</param>
public record OutboxStatistics(
    long PendingCount,
    long ProcessingCount,
    long PublishedCount,
    long FailedCount,
    DateTimeOffset? OldestPendingMessage);

/// <summary>Provides aggregate statistics about the current outbox queue state.</summary>
public interface IOutboxStatisticsReader
{
    /// <summary>Returns a snapshot of current outbox queue statistics.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OutboxStatistics"/> snapshot.</returns>
    Task<OutboxStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}
