using MicroKit.Resilience.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Resilience.Builder;

/// <summary>
/// Builder for fluently configuring resilience strategies in the dependency injection container.
/// </summary>
public sealed class MicroKitResilienceBuilder
{
    /// <summary>
    /// Gets the service collection to which resilience services are being registered.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MicroKitResilienceBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <exception cref="ArgumentNullException">Thrown when services is null.</exception>
    public MicroKitResilienceBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Services.AddSingleton<IResilienceErrorDetector, CompositeResilienceDetector>();
    }

    /// <summary>
    /// Registers a resilience strategy detector with the builder.
    /// </summary>
    /// <typeparam name="TDetector">The type of the detector to register.</typeparam>
    /// <returns>The current builder instance for method chaining.</returns>
    public MicroKitResilienceBuilder AddDetector<TDetector>()
        where TDetector : class, IResilienceStrategyDetector
    {
        Services.AddSingleton<IResilienceStrategyDetector, TDetector>();
        return this;
    }
}
