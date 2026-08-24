namespace MicroKit.Messaging;

/// <summary>
/// Claim and settlement operations required by the outbox processor: one atomic claim
/// reserves a whole batch, one settlement writes every disposition back.
/// </summary>
/// <remarks>
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
/// <see cref="ClaimBatchAsync"/>, and the processor has no business seeing
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
