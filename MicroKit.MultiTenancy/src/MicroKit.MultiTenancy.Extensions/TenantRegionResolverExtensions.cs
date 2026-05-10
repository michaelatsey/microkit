using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Cache;
using MicroKit.MultiTenancy.Configuration;
using MicroKit.MultiTenancy.EndpointProviders;
using MicroKit.MultiTenancy.RegionResolvers;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.MultiTenancy.Extensions;

public static class TenantRegionResolverExtensions
{
    public static MicroKitMultiTenantBuilder WithClaimsTenantRegionResolver(
        this MicroKitMultiTenantBuilder builder)
    {
        ReplaceResolver<ClaimsTenantRegionResolver>(builder);

        return builder;
    }
    public static MicroKitMultiTenantBuilder WithDatabaseTenantRegionResolver(
        this MicroKitMultiTenantBuilder builder)
    {
        ReplaceResolver<DatabaseTenantRegionResolver>(builder);

        return builder;
    }

    public static MicroKitMultiTenantBuilder WithConfigurationTenantRegionResolver(
        this MicroKitMultiTenantBuilder builder, Action<TenantRegionOptions>? configuration = null)
    {
            builder.Services
                .AddOptions<TenantRegionOptions>()
                .Configure(options => configuration?.Invoke(options))
                .ValidateDataAnnotations() // Active les attributs [Range], [Required], etc.
                .ValidateOnStart();

        ReplaceResolver<ConfigurationTenantRegionResolver>(builder);

        return builder;
    }

    public static MicroKitMultiTenantBuilder WithCompositeTenantRegionResolver(
        this MicroKitMultiTenantBuilder builder,
        params Type[] resolvers)
    {
        builder.Services.AddScoped<ITenantRegionResolver>(sp =>
        {
            var instances = resolvers
                .Select(r => (ITenantRegionResolver)sp.GetRequiredService(r))
                .ToList();

            return new CompositeTenantRegionResolver(instances);
        });

        return builder;
    }

    private static void ReplaceResolver<TResolver>(MicroKitMultiTenantBuilder builder)
        where TResolver : class, ITenantRegionResolver
    {
        var descriptor = builder.Services
            .FirstOrDefault(x => x.ServiceType == typeof(ITenantRegionResolver));

        if (descriptor != null)
            builder.Services.Remove(descriptor);

        builder.Services.AddScoped<ITenantRegionResolver, TResolver>();
    }
}
