namespace MicroKit.Messaging;

/// <summary>
/// Represents the lifecycle state of a message in the transactional outbox.
/// </summary>
/// <remarks>
/// State machine: <c>Pending → Processing → Published</c> (happy path);
/// <c>Pending → Processing → Pending</c> (transient failure — <c>RetryCount</c> incremented,
/// <c>NextRetryAtUtc</c> set via exponential back-off);
/// <c>Pending → Processing → Failed</c> (terminal — max retries exceeded,
/// <c>DeadLettered = true</c> always set simultaneously on <see cref="OutboxMessage"/>).
/// </remarks>
public enum OutboxMessageStatus
{
    /// <summary>
    /// Written to the outbox; not yet dispatched. Eligible for lease acquisition
    /// when <c>NextRetryAtUtc</c> is <see langword="null"/> or has elapsed,
    /// and no lease is currently held (<c>LockedUntilUtc &lt; UtcNow</c> or null).
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
    /// Maximum retries exceeded. Terminal — <c>DeadLettered = true</c> is always set
    /// simultaneously on the <see cref="OutboxMessage"/>. Use
    /// <see cref="IOutboxProcessorStore.RequeueAsync"/> for operator-driven reprocessing.
    /// </summary>
    Failed,
}
