namespace MicroKit.Multitenancy;

/// <summary>Read-only registry of known tenants.</summary>
public interface ITenantStore
{
    /// <summary>Finds a tenant by its identifier.</summary>
    /// <param name="tenantId">The tenant identifier to look up.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// A successful result containing the <see cref="ITenantInfo"/>,
    /// or a failure result if the tenant does not exist.
    /// </returns>
    ValueTask<Result<ITenantInfo>> FindAsync(TenantId tenantId, CancellationToken ct = default);

    /// <summary>Returns all registered tenants.</summary>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>A read-only list of all registered <see cref="ITenantInfo"/> instances.</returns>
    ValueTask<IReadOnlyList<ITenantInfo>> ListAllAsync(CancellationToken ct = default);
}
