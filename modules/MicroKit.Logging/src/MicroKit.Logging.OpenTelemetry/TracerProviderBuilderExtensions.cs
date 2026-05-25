namespace MicroKit.Logging.OpenTelemetry;

/// <summary>
/// Extension methods for <see cref="TracerProviderBuilder"/> to integrate MicroKit.Logging ActivitySources.
/// </summary>
public static class TracerProviderBuilderExtensions
{
    /// <summary>
    /// Adds MicroKit.Logging ActivitySources to the tracer provider so that
    /// <c>OperationScope.Begin</c> and <c>EnrichmentPipeline.Execute</c> activities
    /// appear in OpenTelemetry trace exports.
    /// </summary>
    /// <param name="builder">The tracer provider builder to configure.</param>
    /// <returns>The original <paramref name="builder"/> for chaining.</returns>
    public static TracerProviderBuilder AddMicroKitLoggingSources(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddSource(MicroKitActivitySources.Logging.Name)
            .AddSource(MicroKitActivitySources.Enrichment.Name);
    }
}
