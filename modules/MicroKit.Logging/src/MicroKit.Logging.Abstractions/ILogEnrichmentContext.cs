namespace MicroKit.Logging;

/// <summary>
/// Mutable property bag passed to enrichers during the enrichment pipeline.
/// Enrichers write contextual properties into this context; the pipeline applies them to the log entry.
/// </summary>
/// <remarks>
/// Property names must be constants from <see cref="LogPropertyNames"/>.
/// An enricher may write at most <see cref="LoggingConstants.MaxPropertiesPerEnricher"/> properties per invocation.
/// </remarks>
public interface ILogEnrichmentContext
{
    /// <summary>
    /// Sets a property on the log entry. If the property already exists, it is overwritten.
    /// </summary>
    /// <param name="name">
    /// The property name. Must be a <see cref="LogPropertyNames"/> constant.
    /// Names exceeding <see cref="LoggingConstants.MaxPropertyNameLength"/> are rejected.
    /// </param>
    /// <param name="value">The property value. <see langword="null"/> values are allowed.</param>
    void SetProperty(string name, object? value);

    /// <summary>
    /// Sets a property on the log entry only if it has not already been set by a prior enricher.
    /// Use this to avoid overwriting properties set by higher-priority enrichers.
    /// </summary>
    /// <param name="name">
    /// The property name. Must be a <see cref="LogPropertyNames"/> constant.
    /// </param>
    /// <param name="value">The property value.</param>
    /// <returns>
    /// <see langword="true"/> if the property was set; <see langword="false"/> if it already existed.
    /// </returns>
    bool TrySetProperty(string name, object? value);
}
