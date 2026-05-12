using MicroKit.Abstractions.Configuration;
using MicroKit.Abstractions.Serialization;
using MicroKit.Abstractions.Services;
using MicroKit.Core.Serialization;
using MicroKit.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Core;

/// <summary>Extension methods for registering core MicroKit services into an <see cref="IServiceCollection"/>.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core MicroKit infrastructure: default serializer, date-time provider,
    /// and returns a <see cref="MicroKitBuilder"/> for further configuration.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="configure">Optional delegate to configure <see cref="MicroKitOptions"/>.</param>
    /// <returns>A <see cref="MicroKitBuilder"/> for fluent chaining.</returns>
    public static MicroKitBuilder AddMicroKit(this IServiceCollection services, Action<MicroKitOptions>? configure = null)
    {
        services
            .AddOptions<MicroKitOptions>()
            .ValidateOnStart();

        services.TryAddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.TryAddSingleton<IMicroKitSerializer, SystemTextJsonSerializer>();

        var builder = new MicroKitBuilder(services);
        builder.Configure(configure);

        return builder;
    }
}
