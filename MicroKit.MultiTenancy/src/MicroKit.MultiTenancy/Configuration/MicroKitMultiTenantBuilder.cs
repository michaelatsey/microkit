using MicroKit.Abstractions.Contexts;
using MicroKit.MultiTenancy.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.MultiTenancy.Configuration;

public class MicroKitMultiTenantBuilder
{
    public IServiceCollection Services { get; }

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

    public MicroKitMultiTenantBuilder Configure(Action<MicroKitMultiTenancyOptions>? configure = null)
    {
        if(configure is not null)
        {
            Services.Configure(configure);
        }
        return this;
    }

    
}
