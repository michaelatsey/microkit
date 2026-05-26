using System.Diagnostics;
using System.Reflection;

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
    // Exception to "no runtime reflection" rule (CLAUDE.md §3): static field initializer reads
    // assembly metadata once at AppDomain load — standard ActivitySource versioning pattern,
    // not on any hot path.
    private static readonly string s_version =
        typeof(MicroKitActivitySources).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "1.0.0";

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
