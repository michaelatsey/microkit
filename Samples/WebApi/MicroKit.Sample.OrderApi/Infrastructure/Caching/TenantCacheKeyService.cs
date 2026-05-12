using MicroKit.Cqrs.Abstractions.Cache;
using MicroKit.MultiTenancy.Abstractions;

namespace MicroKit.Sample.OrderApi.Infrastructure.Caching;

/// <summary>Builds tenant-scoped cache keys using the current tenant context.</summary>
public class TenantCacheKeyService : ICacheKeyService
{
    private readonly ITenantContext _tenantContext;
    //private readonly IClientContext _clientContext;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="tenant">The current tenant context.</param>
    public TenantCacheKeyService(ITenantContext tenant
        //IClientContext client
        )
    {
        _tenantContext = tenant;
        //_clientContext = client;
    }

    /// <inheritdoc/>
    public string BuildKey(string customKey)
        => $"{_tenantContext.Tenant?.Id}:{customKey}";
}
