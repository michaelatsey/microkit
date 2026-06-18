namespace MicroKit.Messaging;

/// <summary>
/// Topology-agnostic batch engine for the transactional outbox. Locks and dispatches
/// pending outbox messages across all tenants, respecting batch-size limits and the
/// retry strategy. This interface is public so that alternative coordinators
/// (e.g. a per-tenant coordinator in <c>MicroKit.Messaging.Multitenancy</c>) can
/// reuse the engine without reimplementing it.
/// </summary>
/// <remarks>
/// <para>
/// Returns <see cref="Task"/> (not <c>ValueTask</c>) for symmetry with
/// <see cref="IInboxProcessor"/> and BackgroundService chain compatibility.
/// </para>
/// </remarks>
public interface IOutboxProcessor
{
    /// <summary>
    /// Processes up to <paramref name="batchSize"/> pending outbox messages across all
    /// tenants. <c>TenantId</c> is read from each <see cref="OutboxMessage"/> row, never
    /// passed as a filter. Each message is dispatched in its own isolated execution scope
    /// (one scope per message — never shared across a batch).
    /// </summary>
    /// <param name="batchSize">Maximum number of messages to process in this call.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task ProcessBatchAsync(int batchSize, CancellationToken cancellationToken = default);
}
