namespace MicroKit.Messaging.Spi;

/// <summary>
/// Claim and settlement operations required by the outbox processor: one atomic claim
/// reserves a whole batch, one settlement writes every disposition back.
/// </summary>
/// <remarks>
/// <para>
/// Part of the <b>storage SPI</b> (<c>MicroKit.Messaging.Spi</c>): implemented by a persistence
/// provider, resolved by the engine, and never called by application code.
/// </para>
/// <para>
/// <b>Breaking change.</b> Replaces <c>GetPendingAsync</c> / <c>AcquireLeaseAsync</c> /
/// <c>MarkPublishedAsync</c> / <c>MarkFailedAsync</c> / <c>DeadLetterAsync</c>, which
/// cost <c>2N+1</c> round trips per batch, left a contention window between reading a
/// row and leasing it, and — because the terminal writes filtered on message id alone —
/// allowed a processor with an expired lease to overwrite the processor that had taken
/// its messages over.
/// </para>
/// <para>
/// <b>ISP.</b> Administration (<see cref="IOutboxAdminStore"/>) and retention
/// (<see cref="IOutboxRetentionStore"/>) are separate contracts. One implementation may
/// satisfy all three, but a DLQ console has no business seeing
/// <see cref="ClaimBatchAsync(int, TimeSpan, CancellationToken)"/>, and the processor has no business seeing
/// <see cref="IOutboxAdminStore.RequeueAsync"/>.
/// </para>
/// <para>
/// Implementations are batch-scoped and resolved outside the per-message
/// <c>IExecutionScope</c> (ADR-MSG-002, shared-database cross-tenant reservation).
/// </para>
/// </remarks>
public interface IOutboxProcessorStore
{
    /// <summary>
    /// Atomically reserves up to <paramref name="batchSize"/> dispatchable messages and
    /// stamps them with a lease and an ownership token.
    /// </summary>
    /// <param name="batchSize">Maximum number of messages to claim.</param>
    /// <param name="lockDuration">How long the lease is held.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The messages actually won, and the token proving ownership.</returns>
    /// <remarks>
    /// A message is dispatchable when it is pending — or processing with an expired
    /// lease, which is how a crashed processor's work is recovered — is not
    /// dead-lettered, and has reached <see cref="OutboxMessage.NextRetryAtUtc"/>.
    /// Two concurrent processors must never both win the same row, and the count returned
    /// must never exceed <paramref name="batchSize"/>.
    /// <para>
    /// <b>Clock.</b> The lease expiry is computed from the application clock via
    /// <see cref="TimeProvider"/>, not from the database clock. That keeps the implementation
    /// provider-neutral and unit-testable, at the cost of being sensitive to drift between the
    /// application host and the database. Keep <c>LockDuration</c> comfortably larger than any
    /// plausible drift; do not tune it to the second.
    /// </para>
    /// </remarks>
    ValueTask<OutboxClaim> ClaimBatchAsync(
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken ct = default);

    /// <summary>
    /// Claims a batch as <see cref="ClaimBatchAsync(int, TimeSpan, CancellationToken)"/> does,
    /// leaving out the rows of the <paramref name="excluded"/> endpoints: they are in back-off, so the
    /// claim reaches the other endpoint's rows behind them, however many there are, and the down
    /// endpoint's rows are not rewritten while it is down (#188).
    /// </summary>
    /// <param name="batchSize">The maximum number of messages to claim.</param>
    /// <param name="lockDuration">How long the claim holds the messages.</param>
    /// <param name="excluded">The endpoints whose rows are not claimed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The claim; empty when nothing eligible remains.</returns>
    /// <remarks>
    /// The default implementation ignores <paramref name="excluded"/> and claims as the overload
    /// without it. That stays correct — the processor defers an excluded endpoint's rows it is handed,
    /// without an attempt, as <see cref="OutboxOutcomeKind.Deferred"/>, which
    /// <see cref="ApplyOutcomesAsync"/> must settle like every other kind — but every claim then
    /// carries the down endpoint's rows; a store that can filter on <see cref="OutboxMessage.Endpoint"/>
    /// should, with an index led by the status and then the endpoint, so neither the down endpoint's
    /// backlog nor the published history is walked. A row whose endpoint is neither the
    /// transport nor the local endpoint is never excluded: the processor dead-letters it.
    /// </remarks>
    ValueTask<OutboxClaim> ClaimBatchAsync(
        int batchSize,
        TimeSpan lockDuration,
        OutboxEndpoints excluded,
        CancellationToken ct = default)
        => ClaimBatchAsync(batchSize, lockDuration, ct);

    /// <summary>
    /// Persists the disposition of every message from the matching claim, in one call at
    /// the end of the batch.
    /// </summary>
    /// <remarks>
    /// One <i>call</i>, not one statement. The EF implementation groups the set-based
    /// dispositions and issues a separate statement per retried or dead-lettered message,
    /// inside one transaction; the count of statements therefore scales with the number of
    /// <b>failures</b>, not with the batch size. What the contract requires is atomicity and a
    /// single caller-visible round of settlement, not a specific statement count.
    /// </remarks>
    /// <param name="claimToken">
    /// The token from the originating <see cref="OutboxClaim"/>. Every write must filter
    /// on it: a message whose lease expired and was re-claimed elsewhere must be left
    /// untouched rather than silently overwritten.
    /// </param>
    /// <param name="outcomes">
    /// One entry per claimed message. The processor guarantees
    /// <c>outcomes.Count == claim.Count</c>.
    /// </param>
    /// <param name="ct">
    /// A short independent timeout — deliberately <b>not</b> the caller's shutdown token.
    /// Cancelling this write strands every lease in the batch and causes
    /// already-dispatched messages to be dispatched again.
    /// </param>
    /// <returns>
    /// The number of rows actually written. A value below <c>outcomes.Count</c> means
    /// leases were lost mid-batch; the caller logs it rather than assuming success.
    /// </returns>
    ValueTask<int> ApplyOutcomesAsync(
        Guid claimToken,
        IReadOnlyList<OutboxOutcome> outcomes,
        CancellationToken ct = default);
}
