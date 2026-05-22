# Rule: C# Style Guide — MicroKit.MediatR

## Applicabilité
Toujours actif pour tout fichier `.cs` dans ce projet.
Complète `.claude/rules/csharp-style.md` de MicroKit.Result avec les spécificités MediatR.

## Contrats CQRS

### Commands et Queries — sealed record obligatoire
```csharp
// ✅ Immutables par construction
public sealed record CreateOrderCommand(Guid UserId, OrderItem[] Items)
    : ICommand<Result<OrderId>>, IIdempotentCommand
{
    public string IdempotencyKey => $"create-order:{UserId}:{ComputeItemsHash(Items)}";
}

// ❌ class mutable — perd l'immuabilité
public class CreateOrderCommand : ICommand<Result<OrderId>>
{
    public Guid UserId { get; set; } // ❌ setter
}
```

### Handlers — sealed class + primary constructor
```csharp
// ✅
public sealed class CreateOrderHandler(
    IOrderRepository repo,
    IDomainEventDispatcher events)
    : ICommandHandler<CreateOrderCommand, Result<OrderId>>
{ }

// ❌ constructeur old-school avec champs privés
public sealed class CreateOrderHandler : ICommandHandler<...>
{
    private readonly IOrderRepository _repo;
    public CreateOrderHandler(IOrderRepository repo) { _repo = repo; } // ❌ verbeux
}
```

### Retour ValueTask (pas Task)
```csharp
// ✅ ValueTask pour les handlers (souvent synchrone en mémoire, cache hit, etc.)
public async ValueTask<Result<UserDto>> Handle(GetUserQuery query, CancellationToken ct = default)

// ❌ Task — overhead inutile pour les cas synchrones
public async Task<Result<UserDto>> Handle(...)
```

### CancellationToken — toujours en dernier avec default
```csharp
// ✅
ValueTask<Result<T>> Handle(TQuery query, CancellationToken ct = default)

// ❌ sans default — force les appelants à passer CancellationToken.None
ValueTask<Result<T>> Handle(TQuery query, CancellationToken ct)
```

## Behaviors

### BehaviorBase — héritage obligatoire
```csharp
// ✅ Hériter de BehaviorBase (fournit Order, helpers)
public sealed class MyBehavior<TRequest, TResponse>
    : BehaviorBase<TRequest, TResponse>
    where TRequest : IRequest<TResponse>

// ❌ Implémenter IPipelineBehavior<TRequest,TResponse> directement (perd les helpers)
public sealed class MyBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
```

### ConfigureAwait(false) — obligatoire dans la librairie
```csharp
// ✅ Dans tout code de librairie (pas dans les apps consommatrices)
var response = await next().ConfigureAwait(false);
var result = await _service.DoAsync(ct).ConfigureAwait(false);
```

## DomainEvents

### DomainEvent — sealed record pur
```csharp
// ✅ Aucune dépendance infrastructure — données seulement
public sealed record UserRegisteredEvent(
    Guid UserId,
    string Email,
    DateTimeOffset RegisteredAt) : IEvent;
```

### Notification — sealed class héritant DomainEventNotification<T>
```csharp
// ✅ Un seul constructeur qui appelle base
public sealed class UserRegisteredNotification
    : DomainEventNotification<UserRegisteredEvent>
{
    public UserRegisteredNotification(UserRegisteredEvent domainEvent) : base(domainEvent) { }
}
```

## Ordering des membres dans un handler
1. Constructeur (ou primary ctor params)
2. `Handle` — méthode principale
3. Méthodes privées helpers (ordre logique d'appel)

## XML Docs

```csharp
// ✅ Requis sur tous les contrats publics (ICommand, IQuery, markers)
/// <summary>
/// Creates a new order for the specified user.
/// </summary>
/// <param name="UserId">The user placing the order.</param>
/// <param name="Items">The items to include in the order. Must not be empty.</param>
/// <remarks>
/// This command is idempotent — duplicate submissions with the same
/// <see cref="IdempotencyKey"/> will return the original result.
/// </remarks>
public sealed record CreateOrderCommand(Guid UserId, OrderItem[] Items)
    : ICommand<Result<OrderId>>, IIdempotentCommand;
```

## Nommage

| Type | Convention | Exemple |
|---|---|---|
| Command | `{Verb}{Entity}Command` | `CreateOrderCommand` |
| Query | `Get{Entity}[By{Discriminant}]Query` | `GetUserByIdQuery` |
| Stream Query | `Stream{Entities}Query` | `StreamProductsQuery` |
| CommandHandler | `{Verb}{Entity}Handler` | `CreateOrderHandler` |
| QueryHandler | `Get{Entity}Handler` | `GetUserByIdHandler` |
| DomainEvent | `{Entity}{FactPast}Event` | `OrderShippedEvent` |
| Notification | `{Entity}{FactPast}Notification` | `OrderShippedNotification` |
| EventHandler | `{HandlerAction}Handler` | `SendShippingConfirmationHandler` |
| Behavior | `{Concern}Behavior` | `ValidationBehavior` |
| Marker | `I{Concern}Request/Command/Query` | `ICacheableQuery` |
