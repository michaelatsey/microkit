namespace MicroKit.Messaging;

/// <summary>
/// Summary of one <see cref="IInboxProcessor.ProcessBatchAsync"/> run.
/// </summary>
/// <remarks>
/// Returning a result rather than a bare task is what lets the hosting worker adapt its cadence
/// instead of polling on a fixed timer: poll again immediately when saturated, back off
/// geometrically when idle, back off hard on an outage. On a connection-constrained database that
/// saving outweighs the round trips inside a batch.
/// </remarks>
/// <param name="Claimed">Rows atomically claimed from the store.</param>
/// <param name="Processed">Rows whose handler completed successfully.</param>
/// <param name="Retried">Rows that failed transiently and were rescheduled.</param>
/// <param name="DeadLettered">Rows permanently rejected or past the retry ceiling.</param>
/// <param name="Released">Rows claimed but never attempted, returned to the queue untouched.</param>
/// <param name="LeasesLost">
/// Rows skipped because the lease had expired and another processor owned them. A persistently
/// non-zero value means <c>LeaseDuration</c> is shorter than the real handler duration — the
/// single most consequential inbox setting, and otherwise invisible.
/// </param>
/// <param name="AbortReason">Why the batch stopped early, if it did.</param>
public readonly record struct InboxBatchResult(
    int Claimed,
    int Processed,
    int Retried,
    int DeadLettered,
    int Released,
    int LeasesLost,
    InboxBatchAbortReason AbortReason)
{
    /// <summary>Gets an empty result: nothing was claimed.</summary>
    public static InboxBatchResult Empty { get; }

    /// <summary>Gets a value indicating whether the batch claimed at least one row.</summary>
    public bool HasWork => Claimed > 0;

    /// <summary>Gets a value indicating whether the batch stopped early.</summary>
    public bool WasAborted => AbortReason is not InboxBatchAbortReason.None;

    /// <summary>
    /// Determines whether the batch came back full, implying more work is very likely waiting.
    /// </summary>
    /// <param name="batchSize">The batch size that was requested.</param>
    /// <returns><see langword="true"/> when the claim filled the requested batch.</returns>
    public bool IsSaturated(int batchSize) => Claimed >= batchSize;
}
