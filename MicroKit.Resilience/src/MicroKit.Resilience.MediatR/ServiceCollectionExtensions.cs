using MediatR;
using MicroKit.Resilience.Builder;
using MicroKit.Resilience.MediatR.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Resilience.MediatR;

/// <summary>
/// Extension methods for registering resilience MediatR integration into the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="ResilienceBehavior{TRequest,TResponse}"/> MediatR pipeline behavior.
    /// </summary>
    /// <param name="builder">The resilience builder.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when builder is null.</exception>
    public static MicroKitResilienceBuilder AddMicroKitResilienceMediatR(
        this MicroKitResilienceBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(ResilienceBehavior<,>));

        return builder;
    }
}
