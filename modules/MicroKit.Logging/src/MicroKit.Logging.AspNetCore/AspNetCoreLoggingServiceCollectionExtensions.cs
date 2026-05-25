using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Logging.AspNetCore;

/// <summary>
/// Extension methods for registering MicroKit ASP.NET Core logging services.
/// </summary>
public static class AspNetCoreLoggingServiceCollectionExtensions
{
    /// <summary>
    /// Registers MicroKit ASP.NET Core logging services: <see cref="HttpRequestLogEnricher"/>,
    /// <see cref="AspNetCoreLoggingOptions"/>, and the <c>IHttpContextAccessor</c> required
    /// for HTTP context enrichment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call <see cref="LoggingBuilderExtensions.AddMicroKitLogging"/> before this method to
    /// ensure the enrichment pipeline and scope factory are registered.
    /// </para>
    /// <para>
    /// Safe to call multiple times — existing registrations are not overwritten.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configureOptions">Optional delegate to configure <see cref="AspNetCoreLoggingOptions"/>.</param>
    /// <returns>The original <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddMicroKitAspNetCoreLogging(
        this IServiceCollection services,
        Action<AspNetCoreLoggingOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();

        services.TryAddSingleton<AspNetCoreLoggingOptions>(_ =>
        {
            var opts = new AspNetCoreLoggingOptions();
            configureOptions?.Invoke(opts);
            return opts;
        });

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILogEnricher, HttpRequestLogEnricher>());

        return services;
    }
}
