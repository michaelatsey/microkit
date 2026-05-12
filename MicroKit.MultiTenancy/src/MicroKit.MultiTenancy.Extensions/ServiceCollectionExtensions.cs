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

/// <summary>Extension methods for registering MicroKit multi-tenancy services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers core multi-tenancy services including cache, endpoint provider, resolution strategy, and tenant store.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional callback to configure <see cref="MicroKitMultiTenancyOptions"/>.</param>
    /// <returns>A <see cref="MicroKitMultiTenantBuilder"/> for further configuration.</returns>
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
