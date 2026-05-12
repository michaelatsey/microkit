using MicroKit.MultiTenancy.Abstractions;
using Microsoft.AspNetCore.Http;

namespace MicroKit.MultiTenancy.RegionResolvers;

/// <summary>Resolves the tenant region from a JWT claim on the current user.</summary>
public sealed class ClaimsTenantRegionResolver : ITenantRegionResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="httpContextAccessor">Provides access to the current HTTP context.</param>
    public ClaimsTenantRegionResolver(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
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
