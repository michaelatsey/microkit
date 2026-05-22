# Rule: CQRS Patterns — MicroKit.MediatR

## Toujours actif pour tout fichier handler ou command/query.

## Séparation fondamentale

```
ICommand / ICommand<TResult>  → mute l'état, ne lit pas pour retourner
IQuery<TResult>               → lit l'état, ne mute jamais
IStreamQuery<TResult>         → lit l'état en streaming (IAsyncEnumerable)
IEvent / IDomainEventNotification → notification post-fait, pas de réponse
```

**Test décisif :** si tu te demandes si c'est une Command ou une Query → c'est qu'il faut scinder.

## Contrats autorisés

### Commands
```csharp
// ✅ Command sans valeur de retour
public sealed record DeleteUserCommand(Guid UserId) : ICommand;

// ✅ Command qui retourne l'ID de la ressource créée (exception tolérée)
public sealed record CreateOrderCommand(...) : ICommand<Result<OrderId>>;

// ✅ Command avec Result pour les cas qui peuvent échouer
public sealed record UpdateUserCommand(...) : ICommand<Result<Unit>>;

// ❌ Command qui retourne les données de l'entité modifiée
public sealed record UpdateUserCommand(...) : ICommand<Result<UserDto>>; // → scinder en Command + Query
```

### Queries
```csharp
// ✅ Query simple
public sealed record GetUserByIdQuery(Guid UserId) : IQuery<Result<UserDto>>;

// ✅ Query sans Result si never fails (configuration statique, etc.)
public sealed record GetAppConfigQuery() : IQuery<AppConfig>;

// ✅ Stream query pour de gros datasets
public sealed record GetAllProductsQuery(string Category) : IStreamQuery<ProductDto>;

// ❌ Query avec write repository injecté dans le handler
// ❌ Query qui retourne void
// ❌ Query dont le handler publie un DomainEvent
```

## Handlers

### CommandHandler
```csharp
// ✅ Pattern complet
public sealed class CreateOrderHandler(
    IOrderRepository repo,        // write side
    IDomainEventDispatcher events) // pour publier les domain events
    : ICommandHandler<CreateOrderCommand, Result<OrderId>>
{
    public async ValueTask<Result<OrderId>> Handle(
        CreateOrderCommand command,
        CancellationToken ct = default)
    {
        // 1. Charger l'agrégat (via write repo)
        // 2. Appliquer la logique domaine
        // 3. Persister
        // 4. Publier les domain events
        // 5. Retourner le résultat
    }
}
```

### QueryHandler
```csharp
// ✅ Pattern complet — read side uniquement
public sealed class GetUserByIdHandler(
    IUserReadRepository readRepo)  // read-only repo — pas de SaveChanges
    : IQueryHandler<GetUserByIdQuery, Result<UserDto>>
{
    public async ValueTask<Result<UserDto>> Handle(
        GetUserByIdQuery query,
        CancellationToken ct = default)
    {
        // 1. Lire depuis le read model
        // 2. Mapper en DTO
        // 3. Retourner Result
        // Jamais de persistance ici
    }
}
```

### StreamQueryHandler
```csharp
// ✅ IAsyncEnumerable pour les grands volumes
public sealed class GetProductsFeedHandler(IProductReadRepository repo)
    : IStreamQueryHandler<GetProductsFeedQuery, ProductDto>
{
    public async IAsyncEnumerable<ProductDto> Handle(
        GetProductsFeedQuery query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var p in repo.StreamAsync(query.Category, ct).ConfigureAwait(false))
            yield return p.ToDto();
    }
}
```

## Dispatch — comment envoyer commands et queries

```csharp
// ✅ Via les extensions typées (recommandé)
Result<OrderId> result = await _mediator.SendCommandAsync(new CreateOrderCommand(...), ct);
Result<UserDto> user   = await _mediator.SendQueryAsync(new GetUserByIdQuery(id), ct);

// ✅ Via IAsyncEnumerable pour les streams
await foreach (var product in _mediator.StreamQueryAsync(new GetProductsFeedQuery("electronics"), ct))
    yield return product;

// ⚠️ Via IMediator générique — autorisé mais moins explicite
var result = await _mediator.Send(new CreateOrderCommand(...), ct);
```

## DomainEvent — quand et comment

```csharp
// ✅ Dans le handler, APRÈS la persistance
public async ValueTask<Result<OrderId>> Handle(CreateOrderCommand cmd, CancellationToken ct)
{
    var order = Order.Create(cmd.UserId, cmd.Items); // domaine pur
    var id = await _repo.SaveAsync(order, ct);       // persiste
    
    // Publier APRÈS la persistance — les handlers écoutent ce fait accompli
    await _events.PublishAsync(new OrderCreatedEvent(id, cmd.UserId), ct);
    
    return Result.Success(id);
}

// ❌ Publier un domain event depuis un behavior — violation de séparation
// ❌ Publier un domain event AVANT la persistance — le fait n'est pas encore réel
// ❌ Publier un domain event dans un QueryHandler — les queries ne mutent pas
```

## Règles strictes

| ❌ Interdit | ✅ Correct |
|---|---|
| `IMediator` injecté dans un handler | `IDomainEventDispatcher` pour les events |
| Handler Command + Query dans la même classe | Deux handlers séparés |
| `DbContext` dans un QueryHandler | `IReadRepository` ou projection dédiée |
| `void` comme type de retour | `Unit` ou `Result<Unit>` |
| `async Task` | `async ValueTask` |
| CancellationToken sans `default` | `CancellationToken ct = default` |
| `ConfigureAwait` manquant | `.ConfigureAwait(false)` partout dans la lib |
