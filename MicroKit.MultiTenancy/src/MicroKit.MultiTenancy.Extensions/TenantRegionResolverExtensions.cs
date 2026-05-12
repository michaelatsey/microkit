using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Cache;
using MicroKit.MultiTenancy.Configuration;
using MicroKit.MultiTenancy.EndpointProviders;
using MicroKit.MultiTenancy.RegionResolvers;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.MultiTenancy.Extensions;

/// <summary>Extension methods for configuring the tenant region resolver.</summary>
public static class TenantRegionResolverExtensions
{
    /// <summary>Registers <see cref="ClaimsTenantRegionResolver"/> as the active region resolver.</summary>
    /// <param name="builder">The multi-tenancy builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static MicroKitMultiTenantBuilder WithClaimsTenantRegionResolver(
        this MicroKitMultiTenantBuilder builder)
    {
        ReplaceResolver<ClaimsTenantRegionResolver>(builder);

        return builder;
    }
    /// <summary>Registers <c>DatabaseTenantRegionResolver</c> as the active region resolver.</summary>
    /// <param name="builder">The multi-tenancy builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static MicroKitMultiTenantBuilder WithDatabaseTenantRegionResolver(
        this MicroKitMultiTenantBuilder builder)
    {
        ReplaceResolver<DatabaseTenantRegionResolver>(builder);

        return builder;
    }

    /// <summary>Registers <c>ConfigurationTenantRegionResolver</c> as the active region resolver with optional options.</summary>
    /// <param name="builder">The multi-tenancy builder.</param>
    /// <param name="configuration">Optional callback to configure <see cref="TenantRegionOptions"/>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
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

    /// <summary>Registers a composite region resolver that chains the specified resolver types in order.</summary>
    /// <param name="builder">The multi-tenancy builder.</param>
    /// <param name="resolvers">The region resolver types to compose.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
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
