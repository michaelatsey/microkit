namespace MicroKit.Messaging;

using Result = global::MicroKit.Result.Result;

/// <summary>
/// Read/write access to the transactional outbox for the background
/// <c>OutboxProcessor</c> only. Never inject into domain handlers —
/// use <see cref="IOutboxWriter"/> there.
/// </summary>
/// <remarks>
/// The ISP split between <see cref="IOutboxWriter"/> and
/// <see cref="IOutboxProcessorStore"/> ensures domain handlers cannot accidentally
/// invoke processor-level operations. The EF Core implementation (<c>EfOutboxStore</c>)
/// implements both interfaces.
/// </remarks>
public interface IOutboxProcessorStore
{
    /// <summary>
    /// Returns pending message candidates eligible for dispatch.
    /// This is a read-only query — no lease is acquired. Call
    /// <see cref="AcquireLeaseAsync"/> on individual candidates afterwards.
    /// Expired leases (<c>LockedUntilUtc &lt; UtcNow</c>) are included.
    /// Processes all tenants — tenant context is read from each
    /// <see cref="OutboxMessage.TenantId"/> row, not passed as a filter.
    /// </summary>
    /// <param name="batchSize">Maximum number of candidates to return.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A read-only list of pending <see cref="OutboxMessage"/> instances.</returns>
    ValueTask<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize, CancellationToken ct = default);

    /// <summary>
    /// Atomically acquires a processing lease on a single pending message using a
    /// single <c>UPDATE WHERE</c> statement. Returns <see langword="true"/> if the
    /// lease was acquired (1 row updated), <see langword="false"/> if another processor
    /// won the race (0 rows updated).
    /// </summary>
    /// <param name="id">The identifier of the message to lock.</param>
    /// <param name="lockExpiry">The UTC time until which the lease is held.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><see langword="true"/> when the lease was successfully acquired;
    /// <see langword="false"/> when another processor already holds the lease.</returns>
    ValueTask<bool> AcquireLeaseAsync(
        MessageId id, DateTimeOffset lockExpiry, CancellationToken ct = default);

    /// <summary>
    /// Marks a message as successfully published. Terminal state.
    /// Sets <c>Status = Published</c> and <c>ProcessedAtUtc = UtcNow</c>.
    /// </summary>
    /// <param name="id">The identifier of the message to mark.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="Result"/> indicating success or failure of the update.</returns>
    ValueTask<Result> MarkPublishedAsync(MessageId id, CancellationToken ct = default);

    /// <summary>
    /// Resets a message to <see cref="OutboxMessageStatus.Pending"/> after a transient
    /// failure. Clears the lease, sets <c>NextRetryAtUtc</c> using exponential back-off
    /// (<c>2^retryCount</c> seconds, capped at 3600 s).
    /// </summary>
    /// <param name="id">The identifier of the message to reset.</param>
    /// <param name="errorMessage">The error that caused the failure.</param>
    /// <param name="retryCount">
    /// The updated retry count after this failure (caller provides
    /// <c>OutboxMessage.RetryCount + 1</c>, avoiding an extra DB read).
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="Result"/> indicating success or failure of the update.</returns>
    ValueTask<Result> MarkFailedAsync(
        MessageId id, string errorMessage, int retryCount, CancellationToken ct = default);

    /// <summary>
    /// Permanently dead-letters a message when the maximum retry count has been exceeded.
    /// Sets <c>Status = Failed</c>, <c>DeadLettered = true</c>, and
    /// <c>ProcessedAtUtc = UtcNow</c>. Terminal state.
    /// </summary>
    /// <param name="id">The identifier of the message to dead-letter.</param>
    /// <param name="reason">The reason for dead-lettering.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="Result"/> indicating success or failure of the update.</returns>
    ValueTask<Result> DeadLetterAsync(
        MessageId id, string reason, CancellationToken ct = default);

    /// <summary>
    /// Deletes <see cref="OutboxMessageStatus.Published"/> messages older than
    /// <paramref name="olderThan"/>. Used by the cleanup background worker.
    /// </summary>
    /// <param name="olderThan">UTC cutoff — messages with <c>ProcessedAtUtc</c> before
    /// this value are eligible for deletion.</param>
    /// <param name="tenantId">Tenant scope for this cleanup.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The number of rows deleted.</returns>
    ValueTask<int> DeleteProcessedAsync(
        DateTimeOffset olderThan, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Returns dead-lettered messages for operator inspection and manual requeue decisions.
    /// </summary>
    /// <param name="batchSize">Maximum number of messages to return.</param>
    /// <param name="tenantId">Tenant scope.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A read-only list of dead-lettered <see cref="OutboxMessage"/> instances.</returns>
    ValueTask<IReadOnlyList<OutboxMessage>> GetDeadLetteredAsync(
        int batchSize, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Re-queues a dead-lettered message by resetting it to
    /// <see cref="OutboxMessageStatus.Pending"/> with <c>DeadLettered = false</c>
    /// and cleared retry state. Enables operator-driven reprocessing.
    /// </summary>
    /// <param name="id">The identifier of the dead-lettered message to requeue.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="Result"/> indicating success or failure of the update.</returns>
    ValueTask<Result> RequeueAsync(MessageId id, CancellationToken ct = default);
}
