using MicroKit.Abstractions.Contexts;

namespace MicroKit.MultiTenancy.Abstractions;

public interface ITenantContext: ITenantIdAccessor
{
    ITenant? Tenant { get; }
    bool IsResolved { get; } // => Tenant != null;
    void EnsureResolved();
    string? ITenantIdAccessor.TenantId => Tenant?.Id;
}
