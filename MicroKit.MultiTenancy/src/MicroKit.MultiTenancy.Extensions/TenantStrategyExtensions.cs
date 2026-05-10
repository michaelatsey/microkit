using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Configuration;
using MicroKit.MultiTenancy.ResolutionStrategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MicroKit.MultiTenancy.Extensions;

public static class TenantStrategyExtensions
{
    public static MicroKitMultiTenantBuilder WithHeaderStrategy(
        this MicroKitMultiTenantBuilder builder, 
        string? headerName = null)
    {

        builder.Services.AddSingleton<ITenantResolutionStrategy>(sp => 
        {
            var options = sp.GetRequiredService<IOptions<MicroKitMultiTenancyOptions>>().Value;
            var strategy = new HeaderResolutionStrategy(headerName ?? options.HeaderName);
            return strategy;

        });
        return builder;
    }

    public static MicroKitMultiTenantBuilder WithJwtClaimStrategy(
        this MicroKitMultiTenantBuilder builder, 
        string? claimName = null)
    {
        builder.Services.AddSingleton<ITenantResolutionStrategy>(sp =>
        {
            MicroKitMultiTenancyOptions options = sp.GetRequiredService<IOptions<MicroKitMultiTenancyOptions>>().Value;
            var strategy = new JwtClaimResolutionStrategy(claimName ?? options.ClaimNames);
            return strategy;
        });
        return builder;
    }

    
}
