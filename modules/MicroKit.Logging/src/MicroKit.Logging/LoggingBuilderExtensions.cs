using Microsoft.Extensions.DependencyInjection;
using MicroKit.Logging.Internal;

namespace MicroKit.Logging;

/// <summary>
/// Extension methods for registering MicroKit logging services into an <see cref="IServiceCollection"/>.
/// </summary>
public static class LoggingBuilderExtensions
{
    /// <summary>
    /// Adds MicroKit logging services — enrichment pipeline, context accessor, and scope factory —
    /// to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configureOptions">Optional delegate to configure <see cref="MicroKitLoggingOptions"/>.</param>
    /// <param name="configureBuilder">Optional delegate to register enrichers via <see cref="MicroKitLoggingBuilder"/>.</param>
    /// <returns>The original <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddMicroKitLogging(
        this IServiceCollection services,
        Action<MicroKitLoggingOptions>? configureOptions = null,
        Action<MicroKitLoggingBuilder>? configureBuilder = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Apply options
        var opts = new MicroKitLoggingOptions();
        configureOptions?.Invoke(opts);
        services.AddSingleton(opts);

        // Context accessor — singleton backed by static AsyncLocal
        services.AddSingleton<LogContextAccessor>();
        services.AddSingleton<ILogContextAccessor>(sp => sp.GetRequiredService<LogContextAccessor>());

        // Pipeline — receives all ILogEnricher and IAsyncLogEnricher registrations
        services.AddSingleton<EnrichmentPipeline>();

        // Scope factory — single instance satisfies both sync and async interfaces
        services.AddSingleton<LogScopeFactory>();
        services.AddSingleton<ILogScopeFactory>(sp => sp.GetRequiredService<LogScopeFactory>());
        services.AddSingleton<IAsyncLogScopeFactory>(sp => sp.GetRequiredService<LogScopeFactory>());

        // Enricher registrations via builder
        configureBuilder?.Invoke(new MicroKitLoggingBuilder(services));

        return services;
    }
}
