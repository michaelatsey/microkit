using MicroKit.MultiTenancy.Abstractions;
using Microsoft.AspNetCore.Http;

namespace MicroKit.MultiTenancy.RegionResolvers;

public class ClaimsTenantRegionResolver : ITenantRegionResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ClaimsTenantRegionResolver(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ValueTask<string> ResolveAsync(
        string tenantIdentifier,
        CancellationToken cancellationToken = default)
    {
        var region = _httpContextAccessor
            .HttpContext?
            .User?
            .FindFirst("region")?
            .Value;

        return ValueTask.FromResult(region ?? "EU");
    }
}
