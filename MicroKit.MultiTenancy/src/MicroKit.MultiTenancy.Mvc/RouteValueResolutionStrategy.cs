using MicroKit.MultiTenancy.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MicroKit.MultiTenancy.Mvc;

/// <summary>
/// Resolves the tenant identifier from an MVC route value (e.g., a <c>{tenantId}</c> route parameter).
/// </summary>
public sealed class RouteValueResolutionStrategy : ITenantResolutionStrategy
{
    private readonly string _routeKey;
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="routeKey">The route value key to read the tenant identifier from.</param>
    /// <param name="httpContextAccessor">Provides access to the current HTTP context.</param>
    public RouteValueResolutionStrategy(string routeKey, IHttpContextAccessor httpContextAccessor)
    {
        _routeKey = routeKey;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
    public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is null)
            return new ValueTask<string?>((string?)null);

        var value = ctx.GetRouteValue(_routeKey)?.ToString();
        return new ValueTask<string?>(value);
    }
}
