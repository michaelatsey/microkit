namespace MicroKit.Messaging;

/// <summary>
/// Represents the lifecycle state of a message in the transactional outbox.
/// </summary>
/// <remarks>
/// <para>
/// State machine, with every transition out of <see cref="Processing"/> buffered as an
/// <see cref="OutboxOutcome"/> and written by one settlement per batch:
/// </para>
/// <list type="bullet">
///   <item><c>Pending → Processing → Published</c> — dispatch confirmed. Terminal.</item>
///   <item><c>Pending → Processing → Pending</c> (retry) — transient failure below the ceiling.
///         <c>RetryCount</c> incremented and <c>NextRetryAtUtc</c> set to full jitter over an
///         exponential ceiling: <c>Uniform(0, min(2^RetryCount s, MaxRetryBackoff))</c>.</item>
///   <item><c>Pending → Processing → Pending</c> (released) — claimed but never attempted,
///         because the batch aborted on a transport outage, a cancellation or a missing
///         registration. Lease and token cleared and <b>nothing else</b>: no retry consumed.
///         Distinguishing this from a retry is what stops one outage from burning the retry
///         allowance of the whole queue.</item>
///   <item><c>Pending → Processing → Failed</c> — terminal, <c>DeadLettered = true</c> always
///         set in the same write. Reached when the incremented <c>RetryCount</c> reaches
///         <c>MaxRetries</c>, or on the <b>first</b> attempt for an
///         <see cref="OutboxPayloadException"/>.</item>
/// </list>
/// <para>
/// Symmetric with <see cref="InboxMessageStatus"/>. There is no transient <c>Failed</c> state —
/// per-attempt failures reset to <see cref="Pending"/>.
/// </para>
/// </remarks>
public enum OutboxMessageStatus
{
    /// <summary>
    /// Written to the outbox; not yet dispatched. Also the state a message returns to after a
    /// transient failure or a release. Claimable when it is not dead-lettered and
    /// <c>NextRetryAtUtc</c> is <see langword="null"/> or has elapsed.
    /// </summary>
    Pending,

    /// <summary>
    /// Lease acquired by a processor; dispatch in progress.
    /// A message remains in this state until the lock expires (<c>LockedUntilUtc</c>)
    /// or until dispatch is confirmed or fails.
    /// </summary>
    Processing,

    /// <summary>
    /// Delivery confirmed. Terminal — no further processing.
    /// </summary>
    Published,

    /// <summary>
    /// Permanently undeliverable, or the maximum retry count was exceeded. Terminal —
    /// <c>DeadLettered = true</c> is always set simultaneously on the
    /// <see cref="OutboxMessage"/>, and no further retry is attempted. Use
    /// <see cref="IOutboxAdminStore.RequeueAsync"/> for operator-driven reprocessing.
    /// </summary>
    Failed,
}
