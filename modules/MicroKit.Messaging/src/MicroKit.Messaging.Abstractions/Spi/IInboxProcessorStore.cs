namespace MicroKit.Messaging.Spi;

/// <summary>
/// Claim and deferred settlement operations required by the inbox processor.
/// </summary>
/// <remarks>
/// <para>
/// Part of the <b>storage SPI</b> (<c>MicroKit.Messaging.Spi</c>): implemented by a persistence
/// provider, resolved by the engine, and never called by application code.
/// </para>
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
    /// <param name="excluded">
    /// What this store's inbox has in back-off (#124, #210), whose rows must not be claimed: the consumers
    /// in consumer-wide back-off, and the (consumer, tenant) pairs in back-off. Their rows are neither
    /// claimed nor rewritten while the dependency is down, and the rows behind them are reached.
    /// <see cref="InboxClaimExclusions.None"/> leaves nothing out.
    /// </param>
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
    /// <b>The filter is part of the contract.</b> A store that ignores it claims and re-defers a held
    /// backlog, a batch at a time, every time its back-off ends: a pair's rows, or every row with a tenant
    /// of a consumer in consumer-wide back-off — all of a store whose rows carry one tenant. A row with no
    /// tenant (<see cref="InboxMessage.TenantId"/> <see langword="null"/>) is never left out, by either list,
    /// whatever the provider's null semantics: a plain <c>NOT IN</c> over a nullable column leaves it out.
    /// The rows of a pair are those of its <see cref="InboxMessage.ConsumerType"/> and its
    /// <see cref="InboxMessage.TenantId"/>; a store may match them on the two joined by U+001F, which no
    /// consumer key contains (<see cref="InboxClaimExclusions"/>).
    /// </para>
    /// <para>
    /// <b>The bound.</b> Each claim reads, and never writes, the claimable rows of a held pair or
    /// consumer: no claim index orders them by consumer or tenant, so the cost of every claim, for every
    /// other pair, grows with that backlog, not with <paramref name="batchSize"/> — a slower store, not a
    /// stalled one. Matching a pair costs the EF store the reads a tenant cost before #210 — the same plan,
    /// the same buffers, the same rows read past — plus the key comparison per row read: about 40% more
    /// execution time with 20,000 held rows on PostgreSQL in its measurement, once per batch and only while
    /// something is held. <see cref="IInboxAdminStore.DeadLetterPendingAsync"/> is the exit for a tenant
    /// that will not recover; it parks every consumer's rows of the tenant. No call parks one consumer: a
    /// consumer whose own dependency will not recover is removed from the composition, and the retry loop then
    /// dead-letters its rows on sight, as rows of an unknown consumer.
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
        int batchSize, TimeSpan leaseDuration, InboxClaimExclusions excluded, CancellationToken ct = default);

    /// <summary>
    /// Persists the dispositions the handler transaction did not already settle, in one call at
    /// the end of the batch.
    /// </summary>
    /// <remarks>
    /// One <i>call</i>, not one statement — see the equivalent remark on
    /// <see cref="IOutboxProcessorStore.ApplyOutcomesAsync"/>. In normal operation this list is
    /// empty, because a retry that succeeds writes its own row in the handler's transaction
    /// (<see cref="IInboxGuardStore.MarkClaimedProcessedAsync"/>).
    /// </remarks>
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
