using Microsoft.AspNetCore.Http;

namespace MicroKit.MultiTenancy.ResolutionStrategies;

/// <summary>Resolves the tenant identifier from a named JWT claim on the current user.</summary>
public sealed class JwtClaimResolutionStrategy : IHttpTenantResolutionStrategy
{
    private readonly string _claimName;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public JwtClaimResolutionStrategy(string claimName, IHttpContextAccessor httpContextAccessor)
    {
        _claimName = claimName;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
    public Task<string?> GetTenantIdentifierAsync(HttpContext context) =>
        Task.FromResult(context.User.FindFirst(_claimName)?.Value);

    /// <inheritdoc/>
    public Task<string?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var ctx = _httpContextAccessor.HttpContext;
        return ctx is null ? Task.FromResult<string?>(null) : GetTenantIdentifierAsync(ctx);
    }
}
