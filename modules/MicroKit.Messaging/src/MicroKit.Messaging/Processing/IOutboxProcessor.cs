namespace MicroKit.Messaging.Processing;

/// <summary>
/// Topology-agnostic batch engine for the transactional outbox. Atomically claims and
/// dispatches pending outbox messages across all tenants, respecting batch-size limits and
/// the retry strategy.
/// </summary>
/// <remarks>
/// <para>
/// <b>Internal since #185.</b> It was public so a per-tenant coordinator in a separate
/// <c>MicroKit.Messaging.Multitenancy</c> package could reuse this engine (ADR-MSG-002, whose
/// <i>Public surface (v1)</i> section is now superseded). That served the database-per-tenant
/// topology, which tenant fairness (#124) does not cover; the requirement is tracked by #191 as a
/// generic multi-store engine <b>inside</b> Core, so no external package needs this seam.
/// Widening it again is additive if that changes.
/// </para>
/// <para>
/// <b>Breaking change (ADR-MSG-015).</b> Previously returned <see cref="Task"/>. The batch now
/// produces a result the hosting worker needs in order to adapt its cadence; a bare task discards
/// it and leaves the worker on a fixed timer. This superseded the return-type mandate of
/// ADR-MSG-014 for the outbox seam; ADR-MSG-017 then closed the inbox half, so
/// <see cref="IInboxProcessor"/> is now symmetric.
/// </para>
/// </remarks>
internal interface IOutboxProcessor
{
    /// <summary>
    /// Processes up to <paramref name="batchSize"/> dispatchable outbox messages across all
    /// tenants. <c>TenantId</c> is read from each <see cref="OutboxMessage"/> row, never
    /// passed as a filter. Each message is dispatched in its own isolated execution scope
    /// (one scope per message — never shared across a batch).
    /// </summary>
    /// <param name="batchSize">Maximum number of messages to process in this call.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A summary of what happened to every claimed message.</returns>
    ValueTask<OutboxBatchResult> ProcessBatchAsync(int batchSize, CancellationToken cancellationToken = default);
}
