namespace MicroKit.Messaging;

/// <summary>
/// Coordinates one processing cycle of the transactional inbox for a given topology
/// (e.g. Shared-DB, Per-Tenant). Each implementation decides <em>which</em> messages
/// to process and creates the appropriate scopes before delegating to
/// <see cref="IInboxProcessor"/>.
/// </summary>
/// <remarks>
/// <para>
/// The v1 Shared-DB implementation (<c>SharedDbInboxCoordinator</c>) issues a single
/// cross-tenant <c>GetPendingAsync</c> call and delegates to <c>IInboxProcessor</c>.
/// A future per-tenant coordinator (in <c>MicroKit.Messaging.Multitenancy</c>) will
/// iterate over an <c>ITenantSource</c> and create one scope per tenant, reusing the
/// same <see cref="IInboxProcessor"/> engine.
/// </para>
/// <para>
/// Returns <see cref="Task"/> (not <c>ValueTask</c>) for symmetry with
/// <see cref="IOutboxCoordinator"/> and BackgroundService chain compatibility.
/// </para>
/// </remarks>
public interface IInboxCoordinator
{
    /// <summary>Executes one inbox processing cycle.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
