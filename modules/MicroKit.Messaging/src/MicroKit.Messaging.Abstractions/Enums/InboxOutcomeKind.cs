namespace MicroKit.Messaging;

/// <summary>
/// The terminal disposition of a single claimed <see cref="InboxMessage"/> within one batch.
/// </summary>
public enum InboxOutcomeKind
{
    /// <summary>
    /// Handled successfully but <b>not</b> settled by the handler's transaction, because the
    /// handler performed no unit-of-work commit. Written by the deferred path as a fallback,
    /// alongside a warning: for this row the transactional guarantee did not apply and
    /// processing was at-least-once.
    /// </summary>
    /// <remarks>
    /// Rows settled inside the handler transaction never produce an outcome at all — that is
    /// the point of <see cref="IInboxSettlementStore"/>. This member exists only for the
    /// degraded case, and its presence in a batch is a signal worth alerting on.
    /// </remarks>
    Processed = 0,

    /// <summary>
    /// Transient failure below the retry ceiling. Status returns to <c>Received</c>, retry
    /// count is incremented, lease and token cleared, and the row becomes eligible again at
    /// <see cref="InboxOutcome.NextRetryAtUtc"/>.
    /// </summary>
    Retry = 1,

    /// <summary>
    /// Permanently unprocessable. Status becomes <c>Failed</c>,
    /// <see cref="InboxMessage.DeadLettered"/> is set, lease and token cleared.
    /// </summary>
    DeadLetter = 2,

    /// <summary>
    /// Claimed but never attempted — the batch ended early through cancellation, a dependency
    /// outage or a configuration fault. Status returns to <c>Received</c> with the lease
    /// cleared and <b>no</b> retry budget consumed. Distinguishing this from
    /// <see cref="Retry"/> is what stops an outage from burning the retry allowance of every
    /// row in the queue.
    /// </summary>
    Released = 3,
}
