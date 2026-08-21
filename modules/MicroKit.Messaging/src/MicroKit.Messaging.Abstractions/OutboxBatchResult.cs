namespace MicroKit.Messaging;

/// <summary>
/// Summary of one <see cref="IOutboxProcessor.ProcessBatchAsync"/> run.
/// </summary>
/// <remarks>
/// Returning a result rather than a bare task is what lets the hosting worker adapt its cadence
/// instead of polling on a fixed timer: poll again immediately when saturated, back off
/// geometrically when idle, back off hard on an outage. On a connection-constrained database that
/// saving outweighs the round trips inside a batch.
/// </remarks>
/// <param name="Claimed">Messages atomically claimed from the store.</param>
/// <param name="Published">Messages dispatched successfully.</param>
/// <param name="Retried">Messages that failed transiently and were rescheduled.</param>
/// <param name="DeadLettered">Messages permanently rejected or past the retry ceiling.</param>
/// <param name="Released">Messages claimed but never attempted, returned to the queue untouched.</param>
/// <param name="AbortReason">Why the batch stopped early, if it did.</param>
public readonly record struct OutboxBatchResult(
    int Claimed,
    int Published,
    int Retried,
    int DeadLettered,
    int Released,
    OutboxBatchAbortReason AbortReason)
{
    /// <summary>Gets an empty result: nothing was claimed.</summary>
    public static OutboxBatchResult Empty { get; }

    /// <summary>Gets a value indicating whether the batch claimed at least one message.</summary>
    public bool HasWork => Claimed > 0;

    /// <summary>Gets a value indicating whether the batch stopped early.</summary>
    public bool WasAborted => AbortReason is not OutboxBatchAbortReason.None;

    /// <summary>
    /// Determines whether the batch came back full, implying more work is very likely waiting.
    /// </summary>
    /// <param name="batchSize">The batch size that was requested.</param>
    /// <returns><see langword="true"/> when the claim filled the requested batch.</returns>
    public bool IsSaturated(int batchSize) => Claimed >= batchSize;
}
