using MicroKit.MultiTenancy.Abstractions;

namespace MicroKit.MultiTenancy;

/// <summary>Scoped tenant context that holds the resolved tenant for the current request.</summary>
public class TenantContext : ITenantContext, ITenantContextSetter
{
    /// <inheritdoc/>
    public ITenant? Tenant { get; private set; }

    /// <inheritdoc/>
    public bool IsResolved { get; private set; }

    /// <inheritdoc/>
    public void SetTenant(ITenant tenant)
    {
        if (IsResolved)
        {
            throw new InvalidOperationException("Le Tenant a déjà été résolu pour cette requête.");
        }
        Tenant = tenant;
        IsResolved = true;
    }

    /// <inheritdoc/>
    public void EnsureResolved()
    {
        if (!IsResolved)
        {
            throw new InvalidOperationException("Tenant context has not been resolved.");
        }
    }
}
