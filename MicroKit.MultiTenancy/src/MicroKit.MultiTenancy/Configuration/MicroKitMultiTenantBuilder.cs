using MicroKit.Abstractions.Contexts;
using MicroKit.MultiTenancy.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.MultiTenancy.Configuration;

/// <summary>Builder for configuring MicroKit multi-tenancy services.</summary>
public class MicroKitMultiTenantBuilder
{
    /// <summary>Gets the underlying service collection.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Initializes a new instance and registers the core tenant context services.</summary>
    /// <param name="services">The service collection to configure.</param>
    public MicroKitMultiTenantBuilder(IServiceCollection services)
    {
        Services = services;

        // On enregistre l'implémentation concrète
        Services.AddScoped<TenantContext>();

        // On expose les deux interfaces pointant vers la même instance Scoped
        Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        Services.AddScoped<ITenantContextSetter>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantIdAccessor>(sp => sp.GetRequiredService<TenantContext>());
    }

    /// <summary>Applies additional <see cref="MicroKitMultiTenancyOptions"/> configuration.</summary>
    /// <param name="configure">Optional configuration delegate.</param>
    public MicroKitMultiTenantBuilder Configure(Action<MicroKitMultiTenancyOptions>? configure = null)
    {
        if(configure is not null)
        {
            Services.Configure(configure);
        }
        return this;
    }

    
}
