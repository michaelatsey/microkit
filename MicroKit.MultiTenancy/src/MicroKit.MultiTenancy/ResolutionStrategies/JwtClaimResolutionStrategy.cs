using MicroKit.MultiTenancy.Abstractions;
using Microsoft.AspNetCore.Http;

namespace MicroKit.MultiTenancy.ResolutionStrategies;

public class JwtClaimResolutionStrategy : ITenantResolutionStrategy
{
    private readonly string _claimName;

    public JwtClaimResolutionStrategy(string claimName)
    {
        _claimName = claimName;
    }

    public Task<string?> GetTenantIdentifierAsync(HttpContext context)
    {
        return Task.FromResult(context.User.FindFirst(_claimName)?.Value);
    }
}
