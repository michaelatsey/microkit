namespace MicroKit.Logging.OpenTelemetry;

/// <summary>
/// Extension methods for integrating MicroKit.Logging with OpenTelemetry.
/// </summary>
public static class LoggingOpenTelemetryExtensions
{
    /// <summary>
    /// Registers MicroKit OpenTelemetry instrumentation — log record enrichment with
    /// MicroKit ambient context (CorrelationId, OperationId, TenantId, UserId).
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The original <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>Call after <c>AddMicroKitLogging()</c>.</para>
    /// <para>
    /// The consuming application must separately configure the OpenTelemetry logging pipeline
    /// (e.g., <c>services.AddLogging(b => b.AddOpenTelemetry(opts => ...))</c>) and an exporter.
    /// This method only registers the <see cref="MicroKitLogProcessor"/> into that pipeline.
    /// </para>
    /// <para>
    /// For trace integration, call
    /// <see cref="TracerProviderBuilderExtensions.AddMicroKitLoggingSources"/> on the
    /// <see cref="TracerProviderBuilder"/> when setting up OpenTelemetry tracing.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMicroKitOpenTelemetry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<MicroKitLogProcessor>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<OpenTelemetryLoggerOptions>,
            MicroKitOpenTelemetryLoggerOptionsSetup>());

        return services;
    }

    private sealed class MicroKitOpenTelemetryLoggerOptionsSetup(MicroKitLogProcessor processor)
        : IConfigureOptions<OpenTelemetryLoggerOptions>
    {
        public void Configure(OpenTelemetryLoggerOptions options) => options.AddProcessor(processor);
    }
}
