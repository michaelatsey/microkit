# Rule: Pipeline Behaviors — MicroKit.MediatR

## Ordre du pipeline (immuable sans PR)

```
100  LoggingBehavior        — toujours actif, observe tout
200  AuthorizationBehavior  — opt-in via IAuthorizedRequest
300  ValidationBehavior     — opt-in via IValidator<T> enregistré
400  IdempotencyBehavior    — opt-in via IIdempotentCommand (commands uniquement)
500  CachingBehavior        — opt-in via ICacheableQuery (queries uniquement)
600  RetryBehavior          — opt-in via IRetryableRequest
     Handler métier
```

**Règle :** tout nouveau behavior doit déclarer un `PipelineOrder` explicite entre 100 et 999.
Valeurs < 100 et > 999 réservées pour des usages exceptionnels avec justification écrite.

## Règles par behavior

### LoggingBehavior
```csharp
// ✅ Toujours premier — jamais de short-circuit
// ✅ Log structuré : request type, corrélation ID, durée, résultat
// ✅ OpenTelemetry : Activity.Start() + SetTag pour le tracing distribué
// ❌ Ne jamais logger le contenu complet du request (données sensibles)
// ❌ Ne jamais modifier le response
```

### AuthorizationBehavior
```csharp
// ✅ Short-circuit AVANT la validation (fail-fast sécurité)
// ✅ Si Result<T> → retourner Result.Failure(new UnauthorizedError())
// ✅ Si T direct → throw UnauthorizedAccessException
// ❌ Ne pas implémenter la logique d'authorization dans le behavior
//    → déléguer à IAuthorizationService (Microsoft.AspNetCore.Authorization)
```

### ValidationBehavior
```csharp
// ✅ Collect-all : exécuter TOUS les validators, collecter toutes les erreurs
// ✅ Si Result<T> → Result.Failure(ErrorCollection.From(validationErrors))
// ✅ Si T direct → throw ValidationException(errors)
// ✅ Si aucun IValidator<T> enregistré → pass-through silencieux
// ❌ Ne pas throw si Result<T> — incohérence avec le pattern Result
// ❌ Fail-fast sur la première erreur uniquement (perd les autres erreurs)
```

### IdempotencyBehavior
```csharp
// ✅ Commands uniquement (guard : if request is not IIdempotentCommand → next())
// ✅ Stocker la réponse dans IIdempotencyStore après première exécution
// ✅ Retourner la réponse cachée si clé déjà vue (same result, no re-execution)
// ✅ Ne PAS stocker si la réponse est Result.Failure (échec ≠ idempotent success)
// ❌ Ne pas appliquer aux Queries (inutile — les queries n'ont pas d'effets de bord)
// ❌ Clé d'idempotency null ou vide → exception à la configuration
```

### CachingBehavior
```csharp
// ✅ Queries uniquement (guard : if request is not ICacheableQuery → next())
// ✅ Désérialisation type-safe depuis IDistributedCache
// ✅ Ne PAS cacher si le résultat est Result.Failure
// ✅ Respecter ICacheableQuery.Expiry (null = pas d'expiry → WARNING en audit)
// ❌ Ne pas appliquer aux Commands (effets de bord non reproductibles)
// ❌ Cacher des données sensibles sans chiffrement
```

### RetryBehavior
```csharp
// ✅ Polly ResiliencePipeline configuré depuis IRetryableRequest
// ✅ Retry uniquement sur les exceptions transientes (réseau, timeout)
// ✅ Ne PAS retry si Result.IsFailure (échec métier ≠ erreur transiente)
// ✅ Backoff exponentiel par défaut : 1s, 2s, 4s
// ❌ Retry sur ValidationException ou UnauthorizedException
// ❌ MaxRetries = 0 ou négatif → exception à la configuration
```

## Écriture d'un custom behavior

```csharp
// ✅ Pattern obligatoire
public sealed class MyBehavior<TRequest, TResponse>(IDependency dep)
    : BehaviorBase<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    // 1. Ordre explicite
    public override int Order => PipelineOrder.MyBehavior;

    public override async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // 2. Guard opt-in en première ligne
        if (request is not IMyMarker marker)
            return await next().ConfigureAwait(false);

        // 3. ConfigureAwait(false) partout
        var preResult = await dep.DoSomethingAsync(marker, cancellationToken).ConfigureAwait(false);

        // 4. Short-circuit propre si nécessaire
        if (preResult.ShouldBlock)
            return CreateFailureOrThrow<TResponse>(preResult.Error);

        var response = await next().ConfigureAwait(false);

        // 5. Post-processing si nécessaire
        return response;
    }
}
```

## Anti-patterns stricts

```csharp
// ❌ BLOQUANT: behavior qui appelle IMediator
public async Task<TResponse> Handle(...) {
    await _mediator.Send(new AuditCommand()); // → boucle infinie potentielle
}

// ❌ BLOQUANT: behavior qui modifie la request (immuable)
request.SomeProperty = "modified"; // records sont immutables

// ❌ BLOQUANT: deux behaviors avec le même PipelineOrder
public override int Order => 300; // déjà pris par ValidationBehavior

// ❌ BLOQUANT: CachingBehavior sur une Command
if (request is ICacheableQuery) { ... }
// SANS guard command exclusion → peut cacher des effets de bord

// ❌ WARNING: behavior sans test
// Tout behavior doit avoir : PassThrough_WhenMarkerAbsent + AppliesLogic_WhenMarkerPresent
```
