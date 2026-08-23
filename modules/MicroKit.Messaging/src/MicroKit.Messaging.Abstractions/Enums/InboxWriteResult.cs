namespace MicroKit.Messaging;

/// <summary>
/// The outcome of an inbox ingestion attempt.
/// </summary>
/// <remarks>
/// <para>
/// This type exists because the previous <c>AddAsync</c> returned <see langword="void"/> and
/// communicated its most important outcome — "already present, nothing done" — through an
/// exception. That outcome is not a failure: under at-least-once delivery it is the
/// <b>nominal</b> path, reached whenever a lease expires after a crash and the message is
/// redelivered. Using an exception for the expected path meant a correctly delivered message
/// was retried to exhaustion and dead-lettered.
/// </para>
/// <para>
/// An enum rather than a <see langword="bool"/>: <c>if (result is AlreadyPresent)</c> reads on
/// its own, while <c>if (!inserted)</c> requires remembering which way round the convention
/// runs — and this is precisely the decision that went wrong once already.
/// </para>
/// </remarks>
public enum InboxWriteResult
{
    /// <summary>
    /// The row was inserted. First delivery of this message to this consumer; the handler has
    /// not run yet.
    /// </summary>
    Added = 0,

    /// <summary>
    /// The dedup gate held: this message is already recorded for this consumer, so nothing was
    /// written. Normal under at-least-once delivery and never an error — the caller must treat
    /// it as a successful skip, not as a failed dispatch.
    /// </summary>
    /// <remarks>
    /// Named for the state of the world rather than for the database event. "Duplicate"
    /// connotes an anomaly; the anomaly framing is what produced the defect in the first place.
    /// </remarks>
    AlreadyPresent = 1,
}
