namespace MicroKit.Messaging;

/// <summary>
/// Coordinates one processing cycle of the transactional inbox for a given topology
/// (e.g. Shared-DB, Per-Tenant). Each implementation decides <em>which</em> databases a pass
/// covers; the <see cref="IInboxProcessor"/> decides what happens to each row.
/// </summary>
/// <remarks>
/// <para>
/// The v1 Shared-DB implementation (<c>SharedDbInboxCoordinator</c>) issues a single
/// cross-tenant claim and delegates to <see cref="IInboxProcessor"/>. A future per-tenant
/// coordinator (in <c>MicroKit.Messaging.Multitenancy</c>) will iterate over an
/// <c>ITenantSource</c> and create one scope per tenant, reusing the same engine.
/// </para>
/// <para>
/// <b>Breaking change (ADR-MSG-017).</b> Previously returned <see cref="Task"/>, which discarded
/// everything the pass learned and forced the hosting worker onto a fixed timer. Returning the
/// aggregate result is what lets the worker adapt its cadence. This closes the inbox half of the
/// return-type mandate ADR-MSG-014 set and ADR-MSG-015 half-superseded, restoring symmetry with
/// <see cref="IOutboxCoordinator"/>.
/// </para>
/// </remarks>
public interface IInboxCoordinator
{
    /// <summary>Executes one inbox processing cycle across the topology this coordinator owns.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The aggregate outcome of the pass. For a multi-database topology, the sum across every
    /// database covered.
    /// </returns>
    ValueTask<InboxBatchResult> ExecuteAsync(CancellationToken cancellationToken = default);
}
