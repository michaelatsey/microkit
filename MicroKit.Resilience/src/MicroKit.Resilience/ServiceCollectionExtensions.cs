using MicroKit.Resilience.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Resilience;

/// <summary>
/// Extension methods for registering resilience services into the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds MicroKit Resilience infrastructure to the service collection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>A <see cref="MicroKitResilienceBuilder"/> for fluent configuration of resilience strategies.</returns>
    /// <exception cref="ArgumentNullException">Thrown when services is null.</exception>
    public static MicroKitResilienceBuilder AddMicroKitResilience(this IServiceCollection services)
    {
        return services == null
            ? throw new ArgumentNullException(nameof(services))
            : new MicroKitResilienceBuilder(services);
    }
}
