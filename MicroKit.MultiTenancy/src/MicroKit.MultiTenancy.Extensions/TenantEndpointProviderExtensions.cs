using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Cache;
using MicroKit.MultiTenancy.Configuration;
using MicroKit.MultiTenancy.EndpointProviders;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.MultiTenancy.Extensions;

public static class TenantEndpointProviderExtensions
{
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
