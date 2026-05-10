
using MediatR;
using Microsoft.Extensions.Logging;
using MicroKit.Cqrs.Abstractions.Cache;
using MicroKit.Caching.Abstractions;
using MicroKit.Caching;

namespace MicroKit.Cqrs.MediatR.Caching.Pipelines;

public class CachingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, ICacheableRequest
    where TResponse : class
{
    private readonly ICacheService _cache;
    private readonly ICacheKeyService _keyService;
    private readonly ICacheEligibilityChecker _eligibilityChecker;
    private readonly ILogger<CachingBehavior<TRequest, TResponse>> _logger;

    public CachingBehavior(
        ICacheService cache,
        ILogger<CachingBehavior<TRequest, TResponse>> logger,
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
        

        // Étape 2: Vérifier si on bypass le cache
        if (request.Options?.BypassCache == true)
        {
            if(_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Cache bypassed for {Request}", request.GetType().Name);
            }
            
            return await next(cancellationToken);
        }

        // Étape 3: Construire la clé finale
        // Utilisation du service externalisé
        var cacheKey = _keyService.BuildKey(request.CacheKey);

        var cacheDuration = request.CacheDuration ?? TimeSpan.FromMinutes(15);

        // Étape 4: Essayer de récupérer du cache
        var cachedResult = await _cache.GetAsync<TResponse>(cacheKey, cancellationToken);
        if (cachedResult != null)
        {
            if(_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Cache hit for {Key}", cacheKey);
            }
            
            return cachedResult;
        }

        // Étape 5: Exécuter le handler
        if(_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Cache miss for {Key}", cacheKey);
        }
        
        var result = await next(cancellationToken);

        // Étape 6: Mettre en cache si valide
        if (_eligibilityChecker.IsEligible(result))
        {
            var cacheOptions = new CacheOptions
            {
                Duration = request.CacheDuration ?? TimeSpan.FromMinutes(15),
                SlidingExpiration = request.Options?.SlidingExpiration ?? false
            };

            await _cache.SetAsync(cacheKey, result, cacheOptions, cancellationToken);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Cached result for {Key} with duration {Duration}",
                cacheKey, cacheDuration);
            }
            
        }
        return result;
    }
}
