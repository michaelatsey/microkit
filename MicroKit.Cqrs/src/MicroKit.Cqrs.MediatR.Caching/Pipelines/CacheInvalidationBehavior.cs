using MediatR;
using MicroKit.Caching.Abstractions;
using MicroKit.Cqrs.Abstractions.Cache;
using Microsoft.Extensions.Logging;

namespace MicroKit.Cqrs.MediatR.Caching.Pipelines;

public class CacheInvalidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, IRequest<TResponse>, ICacheInvalidatorRequest<TRequest, TResponse>
{
    private readonly ICacheService _cache;
    private readonly ICacheKeyService _keyService; // <--- Stratégie de clé
    private readonly ICacheEligibilityChecker _eligibilityChecker; // <--- Stratégie de succès
    private readonly ILogger<CacheInvalidationBehavior<TRequest, TResponse>> _logger;

    public CacheInvalidationBehavior(
        ICacheService cache, 
        ILogger<CacheInvalidationBehavior<TRequest, TResponse>> logger,
        ICacheKeyService keyService, 
        ICacheEligibilityChecker eligibilityChecker)
    {
        _cache = cache;
        _logger = logger;
        _keyService = keyService;
        _eligibilityChecker = eligibilityChecker;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // On laisse la commande s'exécuter (base de données, métier)
        var response = await next(cancellationToken);

        // Si cancellationToken'est un succès (Ardalis Result), on invalide
        if (_eligibilityChecker.IsEligible(response))
        {
            var keys = request.GetCacheKeys(request, response);
            // On récupère les clés définies dans la commande (ex: CreateTableCommand)
            foreach (var key in keys)
            {
                var fullCacheKey = _keyService.BuildKey(key);
                await _cache.RemoveAsync(fullCacheKey, cancellationToken);
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Cache invalidated: {Key}", fullCacheKey);
                }
            }
        }

        return response;
    }
}
