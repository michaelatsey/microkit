using MicroKit.Cqrs.Abstractions.Cache;
using MicroKit.MultiTenancy.Abstractions;

namespace MicroKit.Sample.OrderApi.Infrastructure.Caching;

public class TenantCacheKeyService : ICacheKeyService
{
    private readonly ITenantContext _tenantContext;
    //private readonly IClientContext _clientContext;

    public TenantCacheKeyService(ITenantContext tenant
        //IClientContext client
        )
    {
        _tenantContext = tenant;
        //_clientContext = client;
    }

    public string BuildKey(string customKey)
        => $"{_tenantContext.Tenant?.Id}:{customKey}";
}
