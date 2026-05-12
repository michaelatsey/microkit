using MicroKit.MultiTenancy.Abstractions;
using Microsoft.AspNetCore.Http;

namespace MicroKit.MultiTenancy.RegionResolvers;

/// <summary>Resolves the tenant region from a JWT claim on the current user.</summary>
public sealed class ClaimsTenantRegionResolver : ITenantRegionResolver
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
