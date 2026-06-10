using Microsoft.Extensions.DependencyInjection;
using MicroKit.Multitenancy;

namespace MicroKit.Auth.Multitenancy;

/// <summary>DI registration extensions for <c>MicroKit.Auth.Multitenancy</c>.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AuthTenantResolutionStrategy"/> as an additional
    /// <see cref="ITenantResolutionStrategy"/> in the multitenancy resolution pipeline.
    /// Does not replace the existing resolver — composes additively by
    /// <see cref="ITenantResolutionStrategy.Order"/>.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddMicroKitAuthMultitenancy(this IServiceCollection services)
    {
        services.AddScoped<ITenantResolutionStrategy, AuthTenantResolutionStrategy>();
        return services;
    }
}
