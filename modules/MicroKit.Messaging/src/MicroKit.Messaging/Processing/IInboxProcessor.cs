namespace MicroKit.Messaging.Processing;

/// <summary>
/// Topology-agnostic batch engine for the transactional inbox. Atomically claims a batch of
/// processable <see cref="InboxMessage"/> rows, deserializes each event, resolves its handler,
/// and settles every disposition.
/// </summary>
/// <remarks>
/// <para>
/// <b>Internal since #185</b>, for the reason recorded on <see cref="IOutboxProcessor"/>: the
/// per-tenant coordinator ADR-MSG-002 kept it public for is superseded, and the
/// database-per-tenant engine it served is #191, inside Core.
/// </para>
/// <para>
/// <b>Breaking change (ADR-MSG-017).</b> Previously returned <see cref="Task"/>. It now returns
/// <see cref="InboxBatchResult"/> so the hosting worker can adapt its cadence, restoring
/// symmetry with <see cref="IOutboxProcessor"/>.
/// </para>
/// <para>
/// The processor must never call <see cref="IInboxWriter.ExistsAsync"/> or
/// <see cref="IInboxWriter.AddAsync"/> — those belong to the ingestion path
/// (<c>EnvelopeReceiver</c>, on the receiving side — the in-process dispatcher this once named
/// was deleted by ADR-MSG-019). The processor is a pure drain loop, and its narrowed
/// dependency on <see cref="IInboxProcessorStore"/> makes that structural rather than a
/// convention.
/// </para>
/// </remarks>
internal interface IInboxProcessor
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
