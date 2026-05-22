# Skill: Pipeline Internals — MicroKit.MediatR

## Quand activer ce skill
- Debugging d'un behavior qui ne s'exécute pas dans le bon ordre
- Conception d'un nouveau behavior avec short-circuit
- Compréhension de l'exécution MediatR sous-jacente
- Optimisation du pipeline pour les hot paths

## Comment MediatR exécute le pipeline

```
_mediator.Send(request)
    │
    ▼
MediatR ServiceFactory résout :
    ├── IEnumerable<IPipelineBehavior<TRequest,TResponse>>  (behaviors, dans l'ordre DI)
    └── IRequestHandler<TRequest,TResponse>                 (handler final)
    │
    ▼
Pipeline chain construction (recursif) :
    Behavior[0].Handle(request, () =>
        Behavior[1].Handle(request, () =>
            Behavior[2].Handle(request, () =>
                Handler.Handle(request, ct))))

IMPORTANT : l'ordre d'exécution = ordre d'enregistrement dans le conteneur DI.
PipelineOrder sert de documentation et de guard de vérification — il n'est pas auto-appliqué.
```

## Ordre DI vs PipelineOrder

```csharp
// ✅ L'ordre d'enregistrement DOIT correspondre à PipelineOrder
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));       // 100
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>)); // 200
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));    // 300
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));   // 400
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));       // 500
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RetryBehavior<,>));         // 600

// ⚠️ Si tu inverses Validation(300) et Authorization(200) dans DI :
// → Authorization s'exécutera APRÈS Validation = moins sécurisé
```

## Détection Result<T> vs T dans les behaviors

```csharp
// Pattern pour créer un échec générique depuis un behavior
// (quand TResponse peut être Result<T> ou T direct)
internal static class BehaviorResultHelper
{
    private static readonly Type ResultOpenType = typeof(Result<>);

    // Crée Result.Failure<T>(error) si TResponse = Result<T>
    // Sinon throw une exception appropriée
    public static TResponse CreateFailureResponse<TResponse>(IError error)
    {
        var responseType = typeof(TResponse);

        if (responseType.IsGenericType &&
            responseType.GetGenericTypeDefinition() == ResultOpenType)
        {
            // Réflexion limitée à l'init — pas dans le hot path
            var valueType = responseType.GetGenericArguments()[0];
            var method = typeof(Result)
                .GetMethod(nameof(Result.Failure), [typeof(IError)])!
                .MakeGenericMethod(valueType);
            return (TResponse)method.Invoke(null, [error])!;
        }

        // TResponse n'est pas Result<T> → on throw
        throw error.Category switch
        {
            ErrorCategory.Unauthorized => new UnauthorizedAccessException(error.Message),
            ErrorCategory.Validation   => new ValidationException(error.Message),
            _                          => new InvalidOperationException(error.Message)
        };
    }

    // Vérifie si une TResponse Result<T> est en échec
    public static bool IsFailure<TResponse>(TResponse response)
    {
        if (response is null) return false;
        var type = typeof(TResponse);
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != ResultOpenType)
            return false;

        // Accès à la propriété IsFailure via réflexion — à cacher au niveau behavior
        return (bool)type.GetProperty(nameof(Result<object>.IsFailure))!.GetValue(response)!;
    }
}
```

## Short-circuit proprement

```csharp
// ✅ Short-circuit qui respecte Result<T> vs T
public override async Task<TResponse> Handle(
    TRequest request,
    RequestHandlerDelegate<TResponse> next,
    CancellationToken ct)
{
    if (request is not IAuthorizedRequest authorized)
        return await next().ConfigureAwait(false);

    var authResult = await _authService.AuthorizeAsync(authorized.RequiredPolicies, ct)
                                       .ConfigureAwait(false);

    if (!authResult.Succeeded)
    {
        // Court-circuit : ne pas appeler next()
        var error = new UnauthorizedError(authResult.FailureReason);
        return BehaviorResultHelper.CreateFailureResponse<TResponse>(error);
    }

    return await next().ConfigureAwait(false);
}
```

## OpenTelemetry dans les behaviors

```csharp
// ✅ Pattern Activity pour le tracing distribué
private static readonly ActivitySource _activitySource =
    new("MicroKit.MediatR", "1.0.0");

public override async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
{
    using var activity = _activitySource.StartActivity(
        $"{typeof(TRequest).Name}",
        ActivityKind.Internal);

    activity?.SetTag("request.type", typeof(TRequest).Name);
    activity?.SetTag("request.assembly", typeof(TRequest).Assembly.GetName().Name);

    try
    {
        var response = await next().ConfigureAwait(false);

        if (BehaviorResultHelper.IsFailure(response))
            activity?.SetStatus(ActivityStatusCode.Error, "Result failure");
        else
            activity?.SetStatus(ActivityStatusCode.Ok);

        return response;
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        throw;
    }
}
```

## Polly dans RetryBehavior

```csharp
// ✅ Construction de la pipeline Polly depuis les paramètres marker
private ResiliencePipeline<TResponse> BuildResiliencePipeline(IRetryableRequest retryable)
{
    return new ResiliencePipelineBuilder<TResponse>()
        .AddRetry(new RetryStrategyOptions<TResponse>
        {
            MaxRetryAttempts = retryable.MaxRetries,
            Delay = retryable.Delay,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            // Ne PAS retry sur les Result.Failure (échec métier)
            ShouldHandle = args => args.Outcome switch
            {
                { Exception: not null } => PredicateResult.True(), // retry sur exception
                { Result: var r } when BehaviorResultHelper.IsFailure(r) =>
                    PredicateResult.False(), // pas de retry sur Result.Failure
                _ => PredicateResult.False()
            }
        })
        .Build();
}
```

## Cache dans CachingBehavior

```csharp
// ✅ Sérialisation type-safe avec System.Text.Json
private async ValueTask<TResponse?> TryGetFromCacheAsync(
    ICacheableQuery cacheableQuery,
    CancellationToken ct)
{
    var cached = await _cache.GetAsync(cacheableQuery.CacheKey, ct).ConfigureAwait(false);
    if (cached is null) return default;

    return JsonSerializer.Deserialize<TResponse>(cached, _jsonOptions);
}

private async ValueTask SetCacheAsync(
    ICacheableQuery cacheableQuery,
    TResponse response,
    CancellationToken ct)
{
    // Ne pas cacher les failures
    if (BehaviorResultHelper.IsFailure(response)) return;

    var options = new DistributedCacheEntryOptions();
    if (cacheableQuery.Expiry.HasValue)
        options.AbsoluteExpirationRelativeToNow = cacheableQuery.Expiry;

    var bytes = JsonSerializer.SerializeToUtf8Bytes(response, _jsonOptions);
    await _cache.SetAsync(cacheableQuery.CacheKey, bytes, options, ct).ConfigureAwait(false);
}
```
