using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Cache;
using MicroKit.MultiTenancy.Configuration;
using MicroKit.MultiTenancy.EndpointProviders;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.MultiTenancy.Extensions;

/// <summary>Extension methods for configuring the tenant endpoint provider.</summary>
public static class TenantEndpointProviderExtensions
{
    /// <summary>Replaces the default endpoint provider with a region-aware implementation.</summary>
    /// <param name="builder">The multi-tenancy builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static MicroKitMultiTenantBuilder WithRegionAwareEndpointProvider(this MicroKitMultiTenantBuilder builder)
    {
        ReplaceResolver<RegionAwareTenantEndpointProvider>(builder);
        return builder;
    }

    private static void ReplaceResolver<TResolver>(MicroKitMultiTenantBuilder builder)
        where TResolver : class, ITenantEndpointProvider
    {
        var descriptor = builder.Services
            .FirstOrDefault(x => x.ServiceType == typeof(ITenantEndpointProvider));

        if (descriptor != null)
            builder.Services.Remove(descriptor);

        builder.Services.AddScoped<ITenantEndpointProvider, TResolver>();
    }
}
