namespace MicroKit.Messaging;

/// <summary>
/// Claim and deferred settlement operations required by the inbox processor.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the drain half of the former <c>IInboxStore</c> (<c>GetPendingAsync</c> /
/// <c>MarkProcessingAsync</c> / <c>MarkProcessedAsync</c> / <c>MarkFailedAsync</c> /
/// <c>DeadLetterAsync</c>).
/// </para>
/// <para>
/// The previous <c>MarkProcessingAsync</c> carried no eligibility predicate and returned
/// <c>void</c>: it was an unconditional write, not a lease. Two processors could both claim the
/// same row, both invoke the handler, and neither could tell. The inbox is the system's
/// deduplication mechanism and it double-processed under concurrency.
/// </para>
/// <para>
/// Implementations are batch-scoped and resolved outside the per-message execution scope
/// (ADR-MSG-002, shared-database cross-tenant reservation).
/// </para>
/// </remarks>
public interface IInboxProcessorStore
{
    /// <summary>
    /// Atomically reserves up to <paramref name="batchSize"/> processable rows and stamps them
    /// with a lease and an ownership token.
    /// </summary>
    /// <param name="batchSize">Maximum number of rows to claim.</param>
    /// <param name="leaseDuration">How long the lease is held.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rows actually won, and the token proving ownership.</returns>
    /// <remarks>
    /// <para>
    /// A row is processable when it is received — or processing with an expired lease, which is
    /// how a crashed processor's work is recovered — is not dead-lettered, and has reached its
    /// back-off deadline. Two concurrent processors must never both win the same row, and the
    /// count returned must never exceed <paramref name="batchSize"/>.
    /// </para>
    /// <para>
    /// <b>Clock.</b> The expiry is computed from the application clock via
    /// <see cref="TimeProvider"/>, not from the database clock. That keeps the implementation
    /// provider-neutral and unit-testable, at the cost of being sensitive to drift between the
    /// application host and the database. Keep <c>LeaseDuration</c> comfortably larger than any
    /// plausible drift; do not tune it to the second.
    /// </para>
    /// </remarks>
    ValueTask<InboxClaim> ClaimBatchAsync(
        int batchSize, TimeSpan leaseDuration, CancellationToken ct = default);

    /// <summary>
    /// Persists the dispositions the handler transaction did not already settle, in a single
    /// round trip.
    /// </summary>
    /// <param name="claimToken">
    /// The token from the originating <see cref="InboxClaim"/>. Every write filters on it, so a
    /// row whose lease expired and was re-claimed elsewhere is left untouched rather than
    /// silently overwritten.
    /// </param>
    /// <param name="outcomes">The dispositions to persist.</param>
    /// <param name="ct">
    /// A short independent timeout — deliberately <b>not</b> the caller's shutdown token. If this
    /// write is cancelled, every lease in the batch stays held until it expires.
    /// </param>
    /// <returns>
    /// The number of rows actually written. A value below <c>outcomes.Count</c> means the
    /// unwritten rows no longer carry this batch's token; the caller logs it rather than
    /// assuming success.
    /// </returns>
    ValueTask<int> ApplyOutcomesAsync(
        Guid claimToken, IReadOnlyList<InboxOutcome> outcomes, CancellationToken ct = default);
}
