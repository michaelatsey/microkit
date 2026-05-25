namespace MicroKit.Logging;

/// <summary>
/// General-purpose constants governing limits and constraints in the MicroKit logging system.
/// </summary>
public static class LoggingConstants
{
    /// <summary>
    /// Maximum allowed character length of a log property name.
    /// Property names exceeding this limit are rejected by the enrichment pipeline.
    /// </summary>
    public const int MaxPropertyNameLength = 64;

    /// <summary>
    /// Maximum number of <see cref="ILogEnricher"/> and <see cref="IAsyncLogEnricher"/> instances
    /// allowed in a single pipeline registration. Additional registrations beyond this limit are ignored.
    /// </summary>
    public const int MaxEnrichersPerPipeline = 32;

    /// <summary>
    /// Maximum number of properties a single enricher may write per <see cref="ILogEnrichmentContext.SetProperty"/> invocation.
    /// Enrichers that exceed this limit will have additional writes silently dropped.
    /// </summary>
    public const int MaxPropertiesPerEnricher = 8;
}
