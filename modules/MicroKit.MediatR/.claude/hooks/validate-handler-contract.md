# Hook: Validate Handler Contract

## Déclenchement
Automatiquement après chaque génération ou modification d'un handler.

## Vérifications

### Contrat de type
```
ICommandHandler<TCommand, TResult> ?
  ├── TCommand implements ICommand<TResult> ? ✅ / ❌
  ├── TResult is Result<T> or T (not Result<Result<T>>) ? ✅ / ❌
  └── TCommand is sealed record ? ✅ / ❌

IQueryHandler<TQuery, TResult> ?
  ├── TQuery implements IQuery<TResult> ? ✅ / ❌
  ├── TQuery has no mutable state (no setters) ? ✅ / ❌
  └── Handler has no write repository injected ? ✅ / ❌
```

### Signature Handle
```
✅ ValueTask<TResult> Handle(TCommand command, CancellationToken ct = default)
❌ Task<TResult> Handle(...)                    → utiliser ValueTask
❌ TResult Handle(...)                          → doit être async
❌ Handle(..., CancellationToken ct)            → ct doit avoir = default
```

### Dépendances injectées
```
❌ IMediator injecté dans un handler → couplage indirect, utiliser IDomainEventDispatcher
❌ HttpContext injecté directement   → passer par un service d'abstraction
❌ DbContext injecté dans un QueryHandler → utiliser un IReadRepository
```

### Markers cohérents
```
Si IIdempotentCommand → vérifier que IdempotencyKey n'est pas vide/null
Si ICacheableQuery → vérifier que CacheKey n'est pas vide/null
Si IAuthorizedRequest → vérifier que RequiredPolicies n'est pas vide
```

## Format de sortie

```
✅ Handler contract valid: CreateOrderHandler
   - ICommandHandler<CreateOrderCommand, Result<OrderId>> ✓
   - ValueTask return type ✓
   - CancellationToken with default ✓
   - No IMediator injection ✓
   - IdempotencyKey non-empty ✓

❌ Handler contract violations: GetUserHandler
   - Return type: Task<Result<UserDto>> → should be ValueTask<Result<UserDto>>
   - IMediator injected → replace with IDomainEventDispatcher
```
