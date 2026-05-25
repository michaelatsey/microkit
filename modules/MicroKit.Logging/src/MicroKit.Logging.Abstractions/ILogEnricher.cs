namespace MicroKit.Logging;

/// <summary>
/// Synchronous log enricher. Implementations add contextual properties to log entries via the enrichment pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must be <see langword="sealed"/>. The enrichment pipeline calls <see cref="Enrich"/>
/// only when the log level is already enabled — implementations need not guard with <c>IsEnabled</c>.
/// </para>
/// <para>
/// <see cref="Enrich"/> must not allocate on the fast path when no properties are added.
/// </para>
/// <para>
/// Register via the Core DI builder. Order of execution is determined by <see cref="Order"/>.
/// Use <see cref="LogEnricherOrder"/> constants for well-known priorities.
/// </para>
/// </remarks>
public interface ILogEnricher
{
    /// <summary>
    /// The execution priority of this enricher within the pipeline.
    /// Lower values run first. Use <see cref="LogEnricherOrder"/> constants.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Enriches the given context with additional log properties.
    /// Called only when the log level is enabled — no <c>IsEnabled</c> guard is needed here.
    /// </summary>
    /// <param name="context">The enrichment context to write properties into.</param>
    void Enrich(ILogEnrichmentContext context);
}
