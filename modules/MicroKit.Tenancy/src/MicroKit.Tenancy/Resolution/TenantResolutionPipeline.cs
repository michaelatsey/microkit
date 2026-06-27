namespace MicroKit.Tenancy;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Orchestrates registered <see cref="ITenantResolutionStrategy"/> instances in ascending
/// <see cref="ITenantResolutionStrategy.Order"/>. Short-circuits on the first successful resolution.
/// Never throws — exceptions from misbehaving strategies are caught and logged as warnings.
/// </summary>
public sealed partial class TenantResolutionPipeline : ITenantResolver
{
    private readonly IReadOnlyList<ITenantResolutionStrategy> _strategies;
    private readonly ITenantStore _store;
    private readonly ILogger<TenantResolutionPipeline> _logger;

    /// <summary>
    /// Initializes a new <see cref="TenantResolutionPipeline"/>.
    /// </summary>
    /// <param name="strategies">Registered resolution strategies — sorted by <see cref="ITenantResolutionStrategy.Order"/> at construction.</param>
    /// <param name="store">Tenant registry used to look up the resolved identifier.</param>
    /// <param name="logger">Optional logger. Defaults to a no-op logger when <see langword="null"/>.</param>
    public TenantResolutionPipeline(
        IEnumerable<ITenantResolutionStrategy> strategies,
        ITenantStore store,
        ILogger<TenantResolutionPipeline>? logger = null)
    {
        _strategies = [.. strategies.OrderBy(s => s.Order)];
        _store = store;
        _logger = logger ?? NullLogger<TenantResolutionPipeline>.Instance;
    }

    /// <inheritdoc/>
    public async ValueTask<Result<ITenantInfo>> ResolveAsync(CancellationToken ct = default)
    {
        if (_strategies.Count == 0)
        {
            LogNoStrategiesRegistered(_logger, nameof(ITenantResolutionStrategy));
            return Failure<ITenantInfo>(MultitenancyErrors.ResolutionFailed);
        }

        foreach (var strategy in _strategies)
        {
            Result<TenantId> strategyResult;

            try
            {
                strategyResult = await strategy.TryResolveAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogStrategyException(_logger, ex, strategy.GetType().Name);
                continue;
            }

            if (!strategyResult.IsSuccess)
                continue;

            // Short-circuit: first successful resolution → look up in store.
            // A store miss is a real failure — do not fall back to remaining strategies.
            return await _store.FindAsync(strategyResult.Value, ct).ConfigureAwait(false);
        }

        LogNoTenantResolved(_logger);
        return Failure<ITenantInfo>(MultitenancyErrors.ResolutionFailed);
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning,
        Message = "No {Interface} is registered. Configure at least one resolution strategy via AddMicroKitMultitenancy().")]
    private static partial void LogNoStrategiesRegistered(ILogger logger, string @interface);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning,
        Message = "Strategy {Strategy} threw an unexpected exception. Continuing to next strategy.")]
    private static partial void LogStrategyException(ILogger logger, Exception ex, string strategy);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning,
        Message = "No resolution strategy could identify the current tenant.")]
    private static partial void LogNoTenantResolved(ILogger logger);
}
