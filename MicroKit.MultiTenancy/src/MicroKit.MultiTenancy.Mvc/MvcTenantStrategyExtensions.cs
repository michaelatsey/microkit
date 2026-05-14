using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.MultiTenancy.Mvc;

/// <summary>Extension methods for registering MVC-specific tenant resolution strategies.</summary>
public static class MvcTenantStrategyExtensions
{
    /// <summary>
    /// Registers <see cref="RouteValueResolutionStrategy"/> as the active tenant resolution strategy,
    /// reading the tenant identifier from the named route value (e.g., a <c>{tenantId}</c> route parameter).
    /// </summary>
    /// <param name="builder">The multi-tenancy builder.</param>
    /// <param name="routeKey">The route value key. Defaults to <c>"tenantId"</c>.</param>
    public static MicroKitMultiTenantBuilder WithRouteValueStrategy(
        this MicroKitMultiTenantBuilder builder,
        string routeKey = "tenantId")
    {
        builder.Services.AddSingleton<ITenantResolutionStrategy>(sp =>
        {
            var accessor = sp.GetRequiredService<IHttpContextAccessor>();
            return new RouteValueResolutionStrategy(routeKey, accessor);
        });

        return builder;
    }
}
