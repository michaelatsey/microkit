namespace MicroKit.Messaging.Processing;

/// <summary>
/// Summary of one <see cref="IOutboxProcessor.ProcessBatchAsync"/> run.
/// </summary>
/// <remarks>
/// Returning a result rather than a bare task is what lets the hosting worker adapt its cadence
/// instead of polling on a fixed timer: poll again immediately when saturated, back off
/// geometrically when idle. An endpoint's outage is backed off by the processor, on that endpoint's
/// rows (<see cref="OutboxEndpointBackoff"/>), not by the loop. On a connection-constrained
/// database that saving outweighs the round trips inside a batch.
/// </remarks>
/// <param name="Claimed">Messages atomically claimed from the store.</param>
/// <param name="Published">Messages dispatched successfully.</param>
/// <param name="Retried">Messages that failed transiently and were rescheduled.</param>
/// <param name="DeadLettered">Messages permanently rejected or past the retry ceiling.</param>
/// <param name="Released">
/// Messages claimed and returned with no attempt charged, their retry count unchanged: released
/// (cancellation, a configuration fault) or deferred to the end of their endpoint's back-off — the
/// row whose failure started that back-off included.
/// </param>
/// <param name="AbortReason">
/// Why some claimed rows were not attempted, if any were: the batch stopped, or an endpoint was down.
/// </param>
internal readonly record struct OutboxBatchResult(
    int Claimed,
    int Published,
    int Retried,
    int DeadLettered,
    int Released,
    OutboxBatchAbortReason AbortReason)
{
    /// <summary>
    /// Gets a value indicating whether the store handed over rows of an endpoint, or of a tenant on an
    /// endpoint, the claim asked it to leave out — a store that does not honour the claim's endpoint or
    /// tenant filters, against its contract —
    /// whatever endpoint verdict <see cref="AbortReason"/> holds (cancellation aside): the loop waits
    /// rather than polling again at once. A back-off that began within the batch does not set it:
    /// the rows it deferred wait out their next attempt, and such a store's next batch sets it if it
    /// comes within the back-off (after a saturated batch, which polls again at once).
    /// </summary>
    public bool HandedOverInBackOff { get; init; }

    /// <summary>Gets an empty result: nothing was claimed.</summary>
    public static OutboxBatchResult Empty { get; }

    /// <summary>Gets a value indicating whether the batch claimed at least one message.</summary>
    public bool HasWork => Claimed > 0;

    /// <summary>
    /// Gets a value indicating whether some claimed rows were not attempted — the batch stopped, or
    /// an endpoint was down and its rows were deferred while the other endpoint's went out.
    /// </summary>
    public bool WasAborted => AbortReason is not OutboxBatchAbortReason.None;

    /// <summary>
    /// Determines whether the batch came back full, implying more work is very likely waiting.
    /// </summary>
    /// <param name="batchSize">The batch size that was requested.</param>
    /// <returns><see langword="true"/> when the claim filled the requested batch.</returns>
    /// <remarks>
    /// Ignores <see cref="AbortReason"/>: a batch counts every message it claimed, the deferred ones
    /// included. Polling again at once on a full batch whose endpoint went down within it is safe — its
    /// rows were deferred to a next attempt in the future, so the next claim does not re-claim them;
    /// a store that then hands over the down endpoint's other rows sets
    /// <see cref="HandedOverInBackOff"/>, which the loop tests first. A
    /// <i>released</i> row is claimable at once, which is why the loop tests
    /// <see cref="OutboxBatchAbortReason.Cancelled"/> before this.
    /// </remarks>
    public bool IsSaturated(int batchSize) => Claimed >= batchSize;
}
