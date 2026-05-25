using System.Diagnostics;

namespace MicroKit.Logging;

/// <summary>
/// Static registry of <see cref="ActivitySource"/> instances owned by MicroKit.Logging.
/// Instrumentation listeners subscribe by source name.
/// Re-exported by <c>MicroKit.Logging.Diagnostics</c> as the public observability API.
/// </summary>
/// <remarks>
/// Source names are canonical — never reference the string literals directly.
/// Use <c>MicroKit.Logging.Diagnostics.ActivitySources.LoggingSourceName</c> for OTEL registration.
/// </remarks>
public static class MicroKitActivitySources
{
    private static readonly string s_version =
        typeof(MicroKitActivitySources).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    /// <summary>
    /// ActivitySource for operation scope lifecycle events (<c>OperationScope.Begin</c>).
    /// Source name: <c>MicroKit.Logging</c>.
    /// </summary>
    public static readonly ActivitySource Logging =
        new("MicroKit.Logging", s_version);

    /// <summary>
    /// ActivitySource for enrichment pipeline execution (<c>EnrichmentPipeline.Execute</c>).
    /// Source name: <c>MicroKit.Logging.Enrichment</c>.
    /// </summary>
    public static readonly ActivitySource Enrichment =
        new("MicroKit.Logging.Enrichment", s_version);
}
