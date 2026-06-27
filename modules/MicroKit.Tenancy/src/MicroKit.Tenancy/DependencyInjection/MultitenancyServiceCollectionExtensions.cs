namespace MicroKit.Tenancy;

using Microsoft.Extensions.DependencyInjection;

/// <summary>DI entry point for MicroKit.Tenancy Core.</summary>
public static class MultitenancyServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core MicroKit.Tenancy services:
    /// <list type="bullet">
    /// <item><see cref="ITenantContextAccessor"/> → <see cref="AsyncLocalTenantContextAccessor"/> (Scoped)</item>
    /// <item><see cref="ITenantContext"/> → resolves the active <see cref="ITenantContextAccessor"/> (Scoped)</item>
    /// <item><see cref="ITenantResolver"/> → <see cref="TenantResolutionPipeline"/> (Scoped)</item>
    /// </list>
    /// Use the returned <see cref="MultitenancyBuilder"/> to register a tenant store and resolution strategies.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional fluent configuration callback.</param>
    /// <returns>A <see cref="MultitenancyBuilder"/> for further configuration.</returns>
    public static MultitenancyBuilder AddMicroKitMultitenancy(
        this IServiceCollection services,
        Action<MultitenancyBuilder>? configure = null)
    {
        services.AddScoped<ITenantContextAccessor, AsyncLocalTenantContextAccessor>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<ITenantContextAccessor>());
        services.AddScoped<ITenantResolver, TenantResolutionPipeline>();

        var builder = new MultitenancyBuilder(services);
        configure?.Invoke(builder);

        return builder;
    }
}
