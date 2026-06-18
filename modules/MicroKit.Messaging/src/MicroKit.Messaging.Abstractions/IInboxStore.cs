namespace MicroKit.Messaging;

using Result = global::MicroKit.Result.Result;

/// <summary>
/// Read/write access to the transactional inbox for idempotent message deduplication
/// and handler state tracking.
/// </summary>
/// <remarks>
/// The compound key <c>(MessageId, ConsumerType)</c> is the authoritative deduplication
/// guard, enforced by a unique database constraint. <see cref="ExistsAsync"/> is a
/// fast-path read optimization — under concurrent load the compound PK constraint
/// is the real idempotency gate.
/// </remarks>
public interface IInboxStore
{
    /// <summary>
    /// Fast-path check for whether an inbox entry already exists for the given
    /// message and consumer. This is a read optimization — not the sole concurrency guard.
    /// </summary>
    /// <param name="messageId">The identifier of the original message.</param>
    /// <param name="consumerType">The assembly-qualified CLR type name of the consuming handler.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><see langword="true"/> if an entry with the compound key
    /// <c>(messageId, consumerType)</c> already exists; <see langword="false"/> otherwise.</returns>
    ValueTask<bool> ExistsAsync(
        MessageId messageId, string consumerType, CancellationToken ct = default);

    /// <summary>
    /// Records the receipt of an inbound message. The compound PK
    /// <c>(MessageId, ConsumerType)</c> enforces deduplication — a
    /// <c>DbUpdateException</c> on a unique constraint violation is the real idempotency gate.
    /// </summary>
    /// <param name="message">The inbox message to persist.
    /// <see cref="InboxMessage.TenantId"/> must not be <see langword="null"/> or empty.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the row has been added.</returns>
    ValueTask AddAsync(InboxMessage message, CancellationToken ct = default);

    /// <summary>
    /// Returns pending inbox messages eligible for handler invocation.
    /// Processes all tenants — tenant context is read from each
    /// <see cref="InboxMessage.TenantId"/> row, not passed as a filter.
    /// </summary>
    /// <param name="batchSize">Maximum number of messages to return.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A read-only list of pending <see cref="InboxMessage"/> instances.</returns>
    ValueTask<IReadOnlyList<InboxMessage>> GetPendingAsync(
        int batchSize, CancellationToken ct = default);

    /// <summary>
    /// Acquires a processing lease on an inbox message, transitioning its status to
    /// <see cref="InboxMessageStatus.Processing"/> and setting
    /// <c>LockedUntilUtc = lockUntil</c>.
    /// </summary>
    /// <param name="messageId">The identifier of the original message.</param>
    /// <param name="consumerType">The assembly-qualified CLR type name of the consuming handler.</param>
    /// <param name="lockUntil">The UTC time until which the processing lease is held.
    /// Derived by the caller from <c>InboxProcessorOptions.LeaseDuration</c>.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the lease has been acquired.</returns>
    /// <exception cref="System.Exception">Propagates any store-level exception (e.g. database
    /// error) — callers must not swallow this. Unlike <see cref="MarkProcessedAsync"/> and
    /// <see cref="MarkFailedAsync"/>, this method throws rather than returning a failed
    /// <see cref="Result"/> because acquiring the lease is a non-optional precondition.</exception>
    ValueTask MarkProcessingAsync(
        MessageId messageId, string consumerType, DateTimeOffset lockUntil,
        CancellationToken ct = default);

    /// <summary>
    /// Marks an inbox message as successfully processed. Terminal state for the happy path.
    /// Sets <c>Status = Processed</c> and <c>ProcessedAtUtc = UtcNow</c>.
    /// </summary>
    /// <param name="messageId">The identifier of the original message.</param>
    /// <param name="consumerType">The assembly-qualified CLR type name of the consuming handler.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="Result"/> indicating success or failure of the update.</returns>
    ValueTask<Result> MarkProcessedAsync(
        MessageId messageId, string consumerType, CancellationToken ct = default);

    /// <summary>
    /// Resets an inbox message to <see cref="InboxMessageStatus.Received"/> after a
    /// transient handler failure. Increments <c>RetryCount</c>, clears
    /// <c>LockedUntilUtc</c>, and sets <c>NextRetryAtUtc = UtcNow + 2^retryCount</c>
    /// seconds (capped at 3600 s). Symmetric with
    /// <see cref="IOutboxProcessorStore.MarkFailedAsync"/>.
    /// </summary>
    /// <param name="messageId">The identifier of the original message.</param>
    /// <param name="consumerType">The assembly-qualified CLR type name of the consuming handler.</param>
    /// <param name="errorMessage">The error message from the failed handler invocation.</param>
    /// <param name="retryCount">
    /// The updated retry count after this failure. The caller provides
    /// <c>InboxMessage.RetryCount + 1</c> to avoid an extra database read.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="Result"/> indicating success or failure of the update.</returns>
    ValueTask<Result> MarkFailedAsync(
        MessageId messageId, string consumerType, string errorMessage,
        int retryCount, CancellationToken ct = default);

    /// <summary>
    /// Permanently dead-letters an inbox message when <c>MaxRetries</c> has been
    /// exceeded. Sets <c>Status = Failed</c>, <c>DeadLettered = true</c>, and
    /// <c>ProcessedAtUtc = UtcNow</c>. Terminal — no further retry will be attempted.
    /// Symmetric with <see cref="IOutboxProcessorStore.DeadLetterAsync"/>.
    /// </summary>
    /// <param name="messageId">The identifier of the original message.</param>
    /// <param name="consumerType">The assembly-qualified CLR type name of the consuming handler.</param>
    /// <param name="reason">The reason for dead-lettering (last error message).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="Result"/> indicating success or failure of the update.</returns>
    ValueTask<Result> DeadLetterAsync(
        MessageId messageId, string consumerType, string reason,
        CancellationToken ct = default);
}
