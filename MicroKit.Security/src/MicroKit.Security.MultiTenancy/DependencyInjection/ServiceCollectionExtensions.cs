using MicroKit.MultiTenancy.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Security.MultiTenancy.DependencyInjection;

/// <summary>Extension methods for wiring MicroKit.Security into the MicroKit.MultiTenancy resolution pipeline.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SecurityPrincipalTenantResolutionStrategy"/> as an <see cref="ITenantResolutionStrategy"/>.
    /// Call this after <c>AddMicroKitSecurity()</c> and before <c>AddMicroKitMultiTenancy()</c> to ensure the
    /// security principal is treated as the authoritative tenant source.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddSecurityPrincipalTenantResolution(this IServiceCollection services)
    {
        services.TryAddSingleton<ITenantResolutionStrategy, SecurityPrincipalTenantResolutionStrategy>();
        return services;
    }
}
