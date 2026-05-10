using MicroKit.MultiTenancy.Abstractions;

namespace MicroKit.MultiTenancy;

public class TenantContext : ITenantContext, ITenantContextSetter
{
    public ITenant? Tenant { get; private set; }

    public bool IsResolved { get; private set; }

    public void SetTenant(ITenant tenant)
    {
        if (IsResolved)
        {
            throw new InvalidOperationException("Le Tenant a déjà été résolu pour cette requête.");
        }
        Tenant = tenant;
        IsResolved = true;
    }

    public void EnsureResolved()
    {
        if (!IsResolved)
        {
            throw new InvalidOperationException("Tenant context has not been resolved.");
        }
    }
}
