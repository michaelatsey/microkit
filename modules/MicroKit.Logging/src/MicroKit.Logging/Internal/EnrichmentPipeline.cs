using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MicroKit.Logging.Internal;

/// <summary>
/// Orchestrates all registered <see cref="ILogEnricher"/> and <see cref="IAsyncLogEnricher"/> instances.
/// Enrichers are sorted by <see cref="ILogEnricher.Order"/> at registration time — no sort on Execute.
/// Enricher failures are caught, logged, and skipped — the pipeline never aborts.
/// </summary>
internal sealed class EnrichmentPipeline
{
    private readonly ILogEnricher[] _enrichers;
    private readonly IAsyncLogEnricher[] _asyncEnrichers;
    private readonly string[] _enricherTypeNames;
    private readonly string[] _asyncEnricherTypeNames;
    private readonly ILogger<EnrichmentPipeline> _logger;

    internal EnrichmentPipeline(
        IEnumerable<ILogEnricher> enrichers,
        IEnumerable<IAsyncLogEnricher> asyncEnrichers,
        ILogger<EnrichmentPipeline> logger)
    {
        _logger = logger;

        // Sort at construction — one-time startup cost, not on the hot path
        var syncList = new List<ILogEnricher>(enrichers);
        syncList.Sort(static (a, b) => a.Order.CompareTo(b.Order));
        _enrichers = syncList.Count <= LoggingConstants.MaxEnrichersPerPipeline
            ? syncList.ToArray()
            : syncList.GetRange(0, LoggingConstants.MaxEnrichersPerPipeline).ToArray();

        var asyncList = new List<IAsyncLogEnricher>(asyncEnrichers);
        asyncList.Sort(static (a, b) => a.Order.CompareTo(b.Order));
        _asyncEnrichers = asyncList.Count <= LoggingConstants.MaxEnrichersPerPipeline
            ? asyncList.ToArray()
            : asyncList.GetRange(0, LoggingConstants.MaxEnrichersPerPipeline).ToArray();

        // Cache type names at construction — avoids string allocation on every exception path
        _enricherTypeNames = new string[_enrichers.Length];
        for (int i = 0; i < _enrichers.Length; i++)
            _enricherTypeNames[i] = _enrichers[i].GetType().Name;

        _asyncEnricherTypeNames = new string[_asyncEnrichers.Length];
        for (int i = 0; i < _asyncEnrichers.Length; i++)
            _asyncEnricherTypeNames[i] = _asyncEnrichers[i].GetType().Name;
    }

    /// <summary>
    /// Runs all synchronous enrichers in order. No LINQ, no allocation beyond enricher writes.
    /// Emits <c>MicroKit.Logging.Enrichment.Executed</c> on completion and
    /// <c>MicroKit.Logging.Enrichment.Faulted</c> per failed enricher.
    /// </summary>
    internal void Execute(LogEnrichmentContext context)
    {
        using var activity = MicroKitActivitySources.Enrichment.HasListeners()
            ? MicroKitActivitySources.Enrichment.StartActivity("EnrichmentPipeline.Execute", ActivityKind.Internal)
            : null;

        long startTimestamp = Stopwatch.GetTimestamp();
        var enrichers = _enrichers;
        var ctx = LogContextAccessor.CurrentContext;

        for (int i = 0; i < enrichers.Length; i++)
        {
            try
            {
                enrichers[i].Enrich(context);
            }
            catch (Exception ex)
            {
                _logger.EnricherFailed(_enricherTypeNames[i], ex);
                LoggingDiagnosticEmitter.EmitEnrichmentFaulted(
                    _enricherTypeNames[i], ex,
                    ctx?.OperationId ?? string.Empty);
            }
        }

        LoggingDiagnosticEmitter.EmitEnrichmentExecuted(
            enrichers.Length,
            ctx?.OperationId ?? string.Empty,
            startTimestamp);

        activity?.SetTag(LogPropertyNames.OperationId, ctx?.OperationId);
    }

    /// <summary>
    /// Runs synchronous enrichers first, then all asynchronous enrichers in order.
    /// Emits <c>MicroKit.Logging.Enrichment.Faulted</c> per failed async enricher.
    /// </summary>
    internal async ValueTask ExecuteAsync(LogEnrichmentContext context, CancellationToken ct = default)
    {
        Execute(context);

        var asyncEnrichers = _asyncEnrichers;
        if (asyncEnrichers.Length == 0) return;

        for (int i = 0; i < asyncEnrichers.Length; i++)
        {
            try
            {
                await asyncEnrichers[i].EnrichAsync(context, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.EnricherFailed(_asyncEnricherTypeNames[i], ex);
                LoggingDiagnosticEmitter.EmitEnrichmentFaulted(
                    _asyncEnricherTypeNames[i], ex,
                    LogContextAccessor.CurrentContext?.OperationId ?? string.Empty);
            }
        }
    }
}
