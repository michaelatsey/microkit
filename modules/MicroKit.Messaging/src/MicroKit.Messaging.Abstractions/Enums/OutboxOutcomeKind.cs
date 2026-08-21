namespace MicroKit.Messaging;

/// <summary>
/// The terminal disposition of a single claimed <see cref="OutboxMessage"/> within one batch.
/// </summary>
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
    /// transport outage. Status returns to <c>Pending</c> with the lease cleared and
    /// <b>no</b> retry budget consumed. Distinguishing this from
    /// <see cref="Retry"/> is what stops a broker outage from burning the retry
    /// allowance of every message in the queue.
    /// </summary>
    Released = 3,
}
