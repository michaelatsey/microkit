using MicroKit.Abstractions.Configuration;
using MicroKit.Abstractions.Serialization;
using MicroKit.Core.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Core.Extensions.Serialization;

/// <summary>Extension methods for registering serializer implementations on a <see cref="MicroKitBuilder"/>.</summary>
public static class MicroKitSerializationExtensions
{
    /// <summary>
    /// Registers <c>System.Text.Json</c> as the <see cref="IMicroKitSerializer"/> implementation.
    /// Uses <see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton{TService,TImplementation}"/> so a custom
    /// serializer registered earlier is not overwritten.
    /// </summary>
    /// <param name="builder">The MicroKit builder.</param>
    /// <returns>The same builder for fluent chaining.</returns>
    public static MicroKitBuilder AddSystemTextJson(
        this MicroKitBuilder builder)
    {
        builder.Services.TryAddSingleton<IMicroKitSerializer, SystemTextJsonSerializer>();
        return builder;
    }

    /// <summary>
    /// Registers Newtonsoft.Json as the <see cref="IMicroKitSerializer"/> implementation.
    /// </summary>
    /// <param name="builder">The MicroKit builder.</param>
    /// <returns>The same builder for fluent chaining.</returns>
    public static MicroKitBuilder AddNewtonsoftJson(
        this MicroKitBuilder builder)
    {
        builder.Services.AddSingleton<IMicroKitSerializer, NewtonsoftJsonSerializer>();
        return builder;
    }
}
