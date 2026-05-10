using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Configuration;
using MicroKit.MultiTenancy.ResolutionStrategies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MicroKit.MultiTenancy.Extensions;

public static class TenantStrategyExtensions
{
    /// <summary>
    /// Registers <see cref="HeaderResolutionStrategy"/> as the active tenant resolution strategy.
    /// </summary>
    public static MicroKitMultiTenantBuilder WithHeaderStrategy(
        this MicroKitMultiTenantBuilder builder,
        string? headerName = null)
    {
        builder.Services.AddSingleton<IHttpTenantResolutionStrategy>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MicroKitMultiTenancyOptions>>().Value;
            var accessor = sp.GetRequiredService<IHttpContextAccessor>();
            return new HeaderResolutionStrategy(headerName ?? options.HeaderName, accessor);
        });

        // Also register as the base interface so non-HTTP consumers can resolve it.
        builder.Services.AddSingleton<ITenantResolutionStrategy>(
            sp => sp.GetRequiredService<IHttpTenantResolutionStrategy>());

        return builder;
    }

    /// <summary>
    /// Registers <see cref="JwtClaimResolutionStrategy"/> as the active tenant resolution strategy.
    /// </summary>
    public static MicroKitMultiTenantBuilder WithJwtClaimStrategy(
        this MicroKitMultiTenantBuilder builder,
        string? claimName = null)
    {
        builder.Services.AddSingleton<IHttpTenantResolutionStrategy>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MicroKitMultiTenancyOptions>>().Value;
            var accessor = sp.GetRequiredService<IHttpContextAccessor>();
            return new JwtClaimResolutionStrategy(claimName ?? options.ClaimNames, accessor);
        });

        builder.Services.AddSingleton<ITenantResolutionStrategy>(
            sp => sp.GetRequiredService<IHttpTenantResolutionStrategy>());

        return builder;
    }
}
