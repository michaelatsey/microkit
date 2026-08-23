namespace MicroKit.Messaging;

/// <summary>
/// Represents the lifecycle state of a message in the transactional inbox.
/// </summary>
/// <remarks>
/// <para>
/// State machine (happy path): <c>Received → Processing → Processed</c>. The transition to
/// <see cref="Processed"/> is staged by <c>IInboxSettlementStore.StageProcessedAsync</c> and
/// committed by the handler's own unit of work, so the mark and the handler's database side
/// effects land together or not at all.
/// </para>
/// <para>
/// State machine (handler failure, below MaxRetries): <c>Processing → Received</c>, reset with
/// back-off through a buffered <see cref="InboxOutcomeKind.Retry"/> outcome. The row is
/// re-queued for the next poll cycle once <c>NextRetryAtUtc</c> has elapsed.
/// </para>
/// <para>
/// State machine (permanent failure, or MaxRetries exceeded): <c>Processing → Failed</c>
/// (terminal) with <c>DeadLettered = true</c>, through an
/// <see cref="InboxOutcomeKind.DeadLetter"/> outcome. A failure known to be permanent — an
/// unregistered consumer, an unreadable payload — reaches this on the <b>first</b> attempt
/// rather than after exhausting the retry budget on a verdict already fixed.
/// </para>
/// <para>
/// State machine (claimed but never attempted): <c>Processing → Received</c> through an
/// <see cref="InboxOutcomeKind.Released"/> outcome, with the lease cleared and <b>no</b> retry
/// budget consumed. That is what stops a dependency outage from burning the retry allowance of
/// every row in the queue.
/// </para>
/// <para>
/// Symmetry with <see cref="OutboxMessageStatus"/>: <c>Failed</c> is always terminal and always
/// paired with <c>InboxMessage.DeadLettered = true</c>. There is no transient <c>Failed</c>
/// state — per-attempt failures reset to <see cref="Received"/>.
/// </para>
/// </remarks>
public enum InboxMessageStatus
{
    /// <summary>
    /// Message received and recorded; handler not yet invoked. Also the state a row returns to
    /// after a transient failure or a release. Reached from three places with three different
    /// meanings — retried, released, or requeued from the dead-letter queue — and only one of
    /// them costs a retry.
    /// </summary>
    Received,

    /// <summary>
    /// Claimed: the handler is executing, the lease is held until <c>LockedUntilUtc</c>, and
    /// <c>ClaimToken</c> names the processor that owns the row.
    /// </summary>
    Processing,

    /// <summary>
    /// Handler completed successfully. Terminal.
    /// </summary>
    Processed,

    /// <summary>
    /// Permanently unprocessable, or the maximum retry count was exceeded.
    /// <c>DeadLettered = true</c> is always set simultaneously. Terminal — no further retry will
    /// be attempted. Symmetric with <see cref="OutboxMessageStatus.Failed"/>.
    /// </summary>
    Failed,
}
