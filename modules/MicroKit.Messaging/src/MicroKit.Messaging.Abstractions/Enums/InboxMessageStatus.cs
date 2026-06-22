namespace MicroKit.Messaging;

/// <summary>
/// Represents the lifecycle state of a message in the transactional inbox.
/// </summary>
/// <remarks>
/// <para>
/// State machine (happy path): <c>Received → Processing → Processed</c>.
/// </para>
/// <para>
/// State machine (handler failure, below MaxRetries):
/// <c>Processing → Received</c> (reset with back-off via
/// <c>IInboxStore.MarkFailedAsync</c>). The row is re-queued for the next poll cycle
/// once <c>NextRetryAtUtc</c> has elapsed.
/// </para>
/// <para>
/// State machine (handler failure, MaxRetries exceeded):
/// <c>Processing → Failed</c> (terminal) with <c>DeadLettered = true</c>, set by
/// <c>IInboxStore.DeadLetterAsync</c>.
/// </para>
/// <para>
/// Symmetry with <see cref="OutboxMessageStatus"/>: <c>Failed</c> is always terminal
/// and always paired with <c>InboxMessage.DeadLettered = true</c>. There is no
/// transient <c>Failed</c> state — per-attempt failures reset to <see cref="Received"/>.
/// </para>
/// </remarks>
public enum InboxMessageStatus
{
    /// <summary>
    /// Message received and recorded; handler not yet invoked. Also the state used
    /// after a transient handler failure — <c>MarkFailedAsync</c> resets to this value
    /// and sets <c>NextRetryAtUtc</c> using exponential back-off.
    /// </summary>
    Received,

    /// <summary>
    /// Handler executing; lease held until <c>LockedUntilUtc</c>.
    /// </summary>
    Processing,

    /// <summary>
    /// Handler completed successfully. Terminal.
    /// </summary>
    Processed,

    /// <summary>
    /// Maximum retry count exceeded. <c>DeadLettered = true</c> is always set
    /// simultaneously by <c>IInboxStore.DeadLetterAsync</c>. Terminal — no further
    /// retry will be attempted. Symmetric with <see cref="OutboxMessageStatus.Failed"/>.
    /// </summary>
    Failed,
}
