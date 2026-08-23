namespace MicroKit.Messaging;

/// <summary>
/// Topology-agnostic batch engine for the transactional inbox. Atomically claims a batch of
/// processable <see cref="InboxMessage"/> rows, deserializes each event, resolves its handler,
/// and settles every disposition. This interface is public so that alternative coordinators
/// (e.g. a per-tenant coordinator in <c>MicroKit.Messaging.Multitenancy</c>) can reuse the
/// engine without reimplementing it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Breaking change (ADR-MSG-017).</b> Previously returned <see cref="Task"/>. It now returns
/// <see cref="InboxBatchResult"/> so the hosting worker can adapt its cadence, restoring
/// symmetry with <see cref="IOutboxProcessor"/>.
/// </para>
/// <para>
/// The processor must never call <see cref="IInboxWriter.ExistsAsync"/> or
/// <see cref="IInboxWriter.AddAsync"/> — those belong to the ingestion path
/// (<see cref="IMessagePublisher"/>). The processor is a pure drain loop, and its narrowed
/// dependency on <see cref="IInboxProcessorStore"/> makes that structural rather than a
/// convention.
/// </para>
/// </remarks>
public interface IInboxProcessor
{
    /// <summary>
    /// Claims, handles and settles up to <paramref name="batchSize"/> rows. Each row is handled
    /// in its own isolated execution scope — one per message, never shared across a batch — and
    /// success is settled inside the handler's own transaction.
    /// </summary>
    /// <param name="batchSize">Maximum number of rows to claim in this call.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A summary of what the batch did, used by the worker to set its next interval.</returns>
    ValueTask<InboxBatchResult> ProcessBatchAsync(
        int batchSize, CancellationToken cancellationToken = default);
}
