namespace MicroKit.Messaging;

/// <summary>
/// Topology strategy for one processing pass. Decides which database(s) a pass covers;
/// the <see cref="IOutboxProcessor"/> decides what happens to each message.
/// </summary>
/// <remarks>
/// <b>Breaking change (ADR-MSG-015).</b> Previously returned <see cref="Task"/>, which discarded
/// everything the pass learned and forced the hosting worker to poll on a fixed timer. Returning
/// the aggregate result is what lets the worker adapt its cadence. This superseded the
/// return-type mandate of ADR-MSG-014 for the outbox seam; ADR-MSG-017 then closed the inbox
/// half, so <see cref="IInboxCoordinator"/> is now symmetric.
/// </remarks>
public interface IOutboxCoordinator
{
    /// <summary>Runs one processing pass across the topology this coordinator owns.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The aggregate outcome of the pass. For a multi-database topology, the sum across
    /// every database covered.
    /// </returns>
    ValueTask<OutboxBatchResult> ExecuteAsync(CancellationToken cancellationToken = default);
}
