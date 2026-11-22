namespace MicroKit.Messaging.Processing;

/// <summary>
/// The retry engine of one store's inbox. Atomically claims a batch of rows whose last attempt
/// failed, runs each through the receive pipeline's guarded retry, and settles the failures.
/// </summary>
/// <remarks>
/// <para>
/// <b>Internal since #185</b>, for the reason recorded on <see cref="IOutboxProcessor"/>: the
/// per-tenant coordinator ADR-MSG-002 kept it public for is superseded, and the
/// database-per-tenant topology it served is a partitioned store, inside Core (#191).
/// </para>
/// <para>
/// <b>Breaking change (ADR-MSG-017).</b> Previously returned <see cref="Task"/>. It now returns
/// <see cref="InboxBatchResult"/> so the hosting worker can adapt its cadence, restoring
/// symmetry with <see cref="IOutboxProcessor"/>.
/// </para>
/// <para>
/// It writes no row of a first delivery: a delivery writes its own (#188 D6), and the processor
/// only retries rows already there, through <see cref="IInboxProcessorStore"/>.
/// </para>
/// </remarks>
internal interface IInboxProcessor
{
    /// <summary>
    /// Claims, retries and settles up to <paramref name="batchSize"/> rows. Each attempt runs in its
    /// own scope — never shared across a batch — and a success is settled inside the handler's own
    /// transaction.
    /// </summary>
    /// <param name="batchSize">Maximum number of rows to claim in this call.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A summary of what the batch did, used by the worker to set its next interval.</returns>
    ValueTask<InboxBatchResult> ProcessBatchAsync(
        int batchSize, CancellationToken cancellationToken = default);
}
