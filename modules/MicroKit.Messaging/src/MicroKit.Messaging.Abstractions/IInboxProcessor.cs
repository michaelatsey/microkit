namespace MicroKit.Messaging;

/// <summary>
/// Topology-agnostic batch engine for the transactional inbox. Drains a batch of
/// pending <see cref="InboxMessage"/> rows, acquires leases, deserializes events,
/// resolves handlers, and records the outcome (processed, retry with back-off, or
/// dead-letter). This interface is public so that alternative coordinators
/// (e.g. a per-tenant coordinator in <c>MicroKit.Messaging.Multitenancy</c>) can
/// reuse the engine without reimplementing it.
/// </summary>
/// <remarks>
/// <para>
/// Returns <see cref="Task"/> (not <c>ValueTask</c>) for symmetry with
/// <see cref="IOutboxProcessor"/> and BackgroundService chain compatibility.
/// </para>
/// <para>
/// The processor must never call <c>IInboxStore.ExistsAsync</c> or
/// <c>IInboxStore.AddAsync</c> — those belong to the ingestion path
/// (<c>IMessagePublisher</c>). The processor is a pure drain loop.
/// </para>
/// </remarks>
public interface IInboxProcessor
{
    /// <summary>
    /// Processes up to <paramref name="batchSize"/> pending inbox messages in the
    /// current scope. Each message is handled in its own isolated execution scope
    /// (one <c>IExecutionScope</c> per message — never shared across a batch).
    /// </summary>
    /// <param name="batchSize">Maximum number of messages to process in this call.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task ProcessBatchAsync(int batchSize, CancellationToken cancellationToken = default);
}
