namespace MicroKit.Logging;

/// <summary>
/// Asynchronous log enricher for rare cases that require I/O to resolve property values
/// (e.g., resolving a tenant display name from a cache or database).
/// </summary>
/// <remarks>
/// <para>
/// Prefer <see cref="ILogEnricher"/> for all hot-path enrichment — synchronous enrichers have zero
/// async overhead. Use <see cref="IAsyncLogEnricher"/> only when a property value cannot be determined
/// without awaiting an external call.
/// </para>
/// <para>Implementations must be <see langword="sealed"/>.</para>
/// </remarks>
public interface IAsyncLogEnricher
{
    /// <summary>
    /// The execution priority of this enricher within the pipeline.
    /// Lower values run first. Use <see cref="LogEnricherOrder"/> constants.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Asynchronously enriches the given context with additional log properties.
    /// Called only when the log level is enabled — no <c>IsEnabled</c> guard is needed here.
    /// </summary>
    /// <param name="context">The enrichment context to write properties into.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous enrichment operation.</returns>
    ValueTask EnrichAsync(ILogEnrichmentContext context, CancellationToken ct = default);
}
