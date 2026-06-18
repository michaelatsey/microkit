namespace MicroKit.Messaging;

/// <summary>
/// Coordinates one processing cycle of the transactional outbox for a given topology
/// (e.g. Shared-DB, Per-Tenant). Each implementation decides <em>which</em> messages
/// to process and creates the appropriate scopes before delegating to
/// <see cref="IOutboxProcessor"/>.
/// </summary>
/// <remarks>
/// <para>
/// The v1 Shared-DB implementation (<c>SharedDbOutboxCoordinator</c>) issues a single
/// cross-tenant <c>GetPendingAsync</c> call and delegates to <c>IOutboxProcessor</c>.
/// A future per-tenant coordinator (in <c>MicroKit.Messaging.Multitenancy</c>) will
/// iterate over an <c>ITenantSource</c> and create one scope per tenant, reusing the
/// same <see cref="IOutboxProcessor"/> engine.
/// </para>
/// <para>
/// Returns <see cref="Task"/> (not <c>ValueTask</c>) for symmetry with
/// <see cref="IInboxCoordinator"/> and BackgroundService chain compatibility.
/// </para>
/// </remarks>
public interface IOutboxCoordinator
{
    /// <summary>Executes one outbox processing cycle.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
