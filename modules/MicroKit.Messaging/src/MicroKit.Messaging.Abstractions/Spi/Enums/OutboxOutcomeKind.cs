namespace MicroKit.Messaging.Spi;

/// <summary>
/// The terminal disposition of a single claimed <see cref="OutboxMessage"/> within one batch.
/// </summary>
/// <remarks>
/// <para>
/// Part of the SPI (<c>MicroKit.Messaging.Spi</c>): persisted state a store reads and writes, not
/// an application concern.
/// </para>
/// </remarks>
public enum OutboxOutcomeKind
{
    /// <summary>Dispatched successfully. Status becomes <c>Published</c>, lease cleared.</summary>
    Published = 0,

    /// <summary>
    /// Transient failure below the retry ceiling. Status returns to <c>Pending</c>,
    /// retry count is incremented, lease cleared, and the message becomes eligible
    /// again at <see cref="OutboxOutcome.NextRetryAtUtc"/>.
    /// </summary>
    Retry = 1,

    /// <summary>
    /// Permanently undeliverable. Status becomes <c>Failed</c>,
    /// <see cref="OutboxMessage.DeadLettered"/> is set, lease cleared.
    /// </summary>
    DeadLetter = 2,

    /// <summary>
    /// Claimed but never attempted — the batch ended early through cancellation or a
    /// configuration fault. Status returns to <c>Pending</c> with the lease cleared and
    /// <b>no</b> retry budget consumed. Distinguishing this from
    /// <see cref="Retry"/> is what stops an outage from burning the retry
    /// allowance of every message in the queue.
    /// </summary>
    Released = 3,

    /// <summary>
    /// Not attempted because the message's endpoint is down — its transport unreachable or not
    /// registered, a local consumer's dependency out. Status returns to <c>Pending</c> with the lease
    /// cleared and the message eligible again at <see cref="OutboxOutcome.NextRetryAtUtc"/>, the end
    /// of the endpoint's back-off. <b>Neither</b> the retry count nor the error text changes: no
    /// attempt was made. A store settles deferrals set-based, one statement per distinct
    /// <see cref="OutboxOutcome.NextRetryAtUtc"/>, since an outage defers whole batches.
    /// </summary>
    Deferred = 4,
}
