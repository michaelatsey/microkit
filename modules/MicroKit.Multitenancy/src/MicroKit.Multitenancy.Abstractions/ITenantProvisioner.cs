namespace MicroKit.Multitenancy;

/// <summary>Provisions new tenants in the system.</summary>
public interface ITenantProvisioner
{
    /// <summary>
    /// Provisions a new tenant from the specified request.
    /// </summary>
    /// <param name="request">The provisioning parameters.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// A successful result containing the new <see cref="TenantId"/>,
    /// or a failure result if provisioning could not complete.
    /// </returns>
    ValueTask<Result<TenantId>> ProvisionAsync(
        TenantProvisioningRequest request,
        CancellationToken ct = default);
}
