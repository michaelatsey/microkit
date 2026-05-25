namespace MicroKit.Logging.Diagnostics;

/// <summary>
/// Public observability API for MicroKit.Logging <see cref="ActivitySource"/> instances.
/// Re-exports the instances owned by <see cref="MicroKitActivitySources"/> in the Core package,
/// ensuring a single <see cref="ActivitySource"/> object per source name.
/// </summary>
/// <remarks>
/// Register with OpenTelemetry using the source name constants:
/// <code>
/// tracerProviderBuilder.AddSource(ActivitySources.LoggingSourceName);
/// tracerProviderBuilder.AddSource(ActivitySources.EnrichmentSourceName);
/// </code>
/// </remarks>
public static class ActivitySources
{
    /// <summary>
    /// Source name for operation scope lifecycle activities (<c>OperationScope.Begin</c>).
    /// </summary>
    public const string LoggingSourceName = "MicroKit.Logging";

    /// <summary>
    /// Source name for enrichment pipeline execution activities (<c>EnrichmentPipeline.Execute</c>).
    /// </summary>
    public const string EnrichmentSourceName = "MicroKit.Logging.Enrichment";

    /// <summary>
    /// Gets the <see cref="ActivitySource"/> for operation scope lifecycle events.
    /// </summary>
    public static ActivitySource Logging => MicroKitActivitySources.Logging;

    /// <summary>
    /// Gets the <see cref="ActivitySource"/> for enrichment pipeline execution.
    /// </summary>
    public static ActivitySource Enrichment => MicroKitActivitySources.Enrichment;
}
