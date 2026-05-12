using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Messaging.Abstractions.Inbox;

/// <summary>Persistence contract for managing per-consumer inbox processing state.</summary>
public interface IInboxStateRepository
{
    /// <summary>Atomically locks and returns the next batch of pending state entries for processing.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="consumerName">The consumer group name.</param>
    /// <param name="batchSize">Maximum number of entries to lock and return.</param>
    /// <param name="lockDuration">Duration for which each entry is locked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of locked <see cref="InboxState"/> entries.</returns>
    Task<IReadOnlyList<InboxState>> LockNextBatchAsync(
        string tenantId,
        string consumerName,
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default);

    /// <summary>Marks a set of state entries as successfully consumed.</summary>
    /// <param name="ids">The primary keys of the state entries to mark as consumed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkConsumedAsync(
        IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken = default);

    /// <summary>Records a processing failure for a single state entry and schedules a retry.</summary>
    /// <param name="id">The primary key of the state entry.</param>
    /// <param name="error">The error message describing the failure.</param>
    /// <param name="nextRetryAt">The UTC time at which to retry, or <see langword="null"/> to leave unscheduled.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkFailedAsync(
        string id,
        string error,
        DateTimeOffset? nextRetryAt,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the number of pending (unprocessed) state entries for the given consumer.</summary>
    /// <param name="consumerName">The consumer group name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The count of pending entries.</returns>
    Task<int> GetBacklogCountAsync(
        string consumerName,
        CancellationToken cancellationToken = default);
}
