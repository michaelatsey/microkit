using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Cache;
using MicroKit.MultiTenancy.Configuration;
using MicroKit.MultiTenancy.EndpointProviders;
using MicroKit.MultiTenancy.RegionResolvers;
using MicroKit.MultiTenancy.Stores;
using MicroKit.MultiTenancy.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace MicroKit.MultiTenancy.Extensions;

public static class ServiceCollectionExtensions
{
    public static MicroKitMultiTenantBuilder AddMicroKitMultiTenancy(
        this IServiceCollection services,
        Action<MicroKitMultiTenancyOptions>? configure = null
        )
    {
        services.AddHttpContextAccessor();
        services.AddMemoryCache();

        services
            .AddOptions<MicroKitMultiTenancyOptions>()
            .BindConfiguration(MicroKitMultiTenancyOptions.SectionName) // Optionnel : permet de binder depuis appsettings.json
            //.Configure(options => configure?.Invoke(options))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        // Cache
        services.TryAddSingleton<ITenantCache, DefaultTenantCache>();

        // Endpoint provider
        services.TryAddScoped<ITenantEndpointProvider, DefaultTenantEndpointProvider>();

        // Region resolver
        services.TryAddScoped<ITenantRegionResolver, DefaultTenantRegionResolver>();

        // Default store is a pass-through store that doesn't cache anything. You can replace it with your own implementation that retrieves tenant information from a database or other source.
        services.TryAddScoped<ITenantStore, PassThroughTenantStore>();
        
        services.TryAddScoped<ITenant, Tenant>();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, MultiTenantValidationService>());

        return new MicroKitMultiTenantBuilder(services);
    }
}
