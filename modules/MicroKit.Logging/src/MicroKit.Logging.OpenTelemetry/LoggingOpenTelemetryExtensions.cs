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

        // Do NOT register MicroKitLogProcessor as a DI singleton — OpenTelemetryLoggerProvider
        // takes disposal ownership of every processor it receives. The setup class below constructs
        // a fresh processor per provider build so the provider's lifetime governs disposal.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<OpenTelemetryLoggerOptions>,
            MicroKitOpenTelemetryLoggerOptionsSetup>());

        return services;
    }

    private sealed class MicroKitOpenTelemetryLoggerOptionsSetup(ILogContextAccessor accessor)
        : IConfigureOptions<OpenTelemetryLoggerOptions>
    {
        public void Configure(OpenTelemetryLoggerOptions options)
            => options.AddProcessor(new MicroKitLogProcessor(accessor));
    }
}
