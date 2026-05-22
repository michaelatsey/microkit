# Command: /audit-pipeline

## Usage
```
/audit-pipeline [--assembly <path>] [--verbose]
```

## Description
Analyse l'ensemble des handlers et behaviors enregistrés dans l'application.
Détecte les problèmes de configuration du pipeline : ordre incorrect, behaviors manquants,
handlers mal typés, markers oubliés, violations CQRS.

## Ce qui est vérifié

### Violations CQRS
- [ ] Handler qui implémente à la fois `ICommandHandler` et `IQueryHandler`
- [ ] Command qui hérite de `IQuery` (ou vice versa)
- [ ] Query dont le handler a des effets de bord détectables (write repo injecté)
- [ ] `ICommand` sans handler enregistré
- [ ] `IQuery` sans handler enregistré

### Pipeline & Behaviors
- [ ] Behaviors avec `Order` dupliqué
- [ ] Behaviors enregistrés dans un ordre différent de `PipelineOrder`
- [ ] `ICacheableQuery` implémenté sur une Command (cache sur command = bug)
- [ ] `IIdempotentCommand` implémenté sur une Query (idempotency sur query = inutile)
- [ ] `IRetryableRequest` sans `MaxRetries > 0` défini
- [ ] `ICacheableQuery` sans `CacheKey` non-null/non-vide

### Handlers
- [ ] Handler async sans `CancellationToken` propagé aux appels internes
- [ ] Handler qui catch `Exception` sans mapper en `IError`
- [ ] Handler qui injecte `IMediator` directement (couplage indirect)
- [ ] Handler non `sealed`
- [ ] `ValueTask` retourné sans `ConfigureAwait(false)`

### DomainEvents
- [ ] `DomainEventNotification` sans handler enregistré
- [ ] DomainEvent publié depuis un behavior (violates separation)
- [ ] Handler de DomainEvent qui injecte `IMediator`

### Result<T> consistency
- [ ] Handler qui retourne `Result<T>` mais behavior de validation qui throw au lieu de Failure
- [ ] Mix Result<T> / T dans les handlers d'un même aggregate (inconsistency)

## Output format

```
🔍 Pipeline Audit — MicroKit.MediatR
════════════════════════════════════════

📋 Handlers found: 12 commands, 8 queries, 3 events, 1 stream

🔴 VIOLATIONS (2)
  ├── CreateOrderCommand: IIdempotentCommand.IdempotencyKey returns empty string
  │   Fix: compute a deterministic key from command properties
  └── GetReportsQuery: ICacheableQuery.CacheKey is null
      Fix: return a non-null cache key

🟡 WARNINGS (3)
  ├── UpdateUserCommand: no IValidator<UpdateUserCommand> registered
  │   Consider: add validation or mark as intentionally unvalidated
  ├── GetProductsQuery: CachingBehavior active but no expiry set (infinite cache)
  │   Consider: set ICacheableQuery.Expiry explicitly
  └── SendEmailHandler (domain event): no retry — email send can fail transiently
      Consider: implement IRetryableRequest

✅ OK (18)
  All other handlers and behaviors are correctly configured.

📊 Pipeline order verified:
  LoggingBehavior(100) → AuthorizationBehavior(200) → ValidationBehavior(300)
  → IdempotencyBehavior(400) → CachingBehavior(500) → RetryBehavior(600)
  ✅ No order conflicts detected.
```
