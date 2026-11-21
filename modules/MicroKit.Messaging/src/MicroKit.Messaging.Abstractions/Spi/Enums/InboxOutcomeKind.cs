namespace MicroKit.Messaging.Spi;

/// <summary>
/// The terminal disposition of a single claimed <see cref="InboxMessage"/> within one batch.
/// </summary>
/// <remarks>
/// <para>
/// Part of the SPI (<c>MicroKit.Messaging.Spi</c>): persisted state a store reads and writes, not
/// an application concern.
/// </para>
/// </remarks>
public enum InboxOutcomeKind
{
    /// <summary>
    /// Handled, settled outside the handler's transaction. The engine no longer produces it
    /// (#188): a handled row is written in the handler's transaction. A store still persists it as
    /// <see cref="InboxMessageStatus.Processed"/>.
    /// </summary>
    Processed = 0,

    /// <summary>
    /// Transient failure below the retry ceiling. Status becomes <c>Failed</c>, retry
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
    /// outage or a configuration fault. Status becomes <c>Failed</c> with the lease
    /// cleared and <b>no</b> retry budget consumed. Distinguishing this from
    /// <see cref="Retry"/> is what stops an outage from burning the retry allowance of every
    /// row in the queue.
    /// </summary>
    Released = 3,
}
