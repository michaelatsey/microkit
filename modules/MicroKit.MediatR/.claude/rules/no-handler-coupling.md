# Rule: No Handler Coupling

## Principe
Un handler doit être une unité isolée et testable.
Il ne doit connaître que ses dépendances directes — jamais le pipeline, jamais MediatR lui-même.

## Dépendances autorisées dans un handler

### CommandHandler
```
✅ IWriteRepository<T>          → persistance de l'agrégat
✅ IDomainEventDispatcher       → publication des domain events
✅ IExternalService (interface) → services externes via abstraction
✅ ILogger<T>                   → logging (avec parcimonie)
✅ IDateTimeProvider            → abstraction du temps (testabilité)
✅ IIdGenerator                 → génération d'IDs (testabilité)
```

### QueryHandler
```
✅ IReadRepository<T>           → lecture depuis le read model
✅ IQueryableRepository<T>      → accès IQueryable pour les projections
✅ IDistributedCache            → seulement si le handler gère son propre cache (rare)
✅ ILogger<T>                   → logging
```

### DomainEventHandler
```
✅ Services d'application       → email, SMS, push notification
✅ IWriteRepository<T>          → si le handler crée un autre agrégat
✅ IExternalService (interface) → services externes
```

## Dépendances INTERDITES dans un handler

```csharp
// ❌ IMediator — couplage indirect au pipeline
public sealed class CreateOrderHandler(IMediator mediator) { }
// ✅ IDomainEventDispatcher pour les events

// ❌ HttpContext — couplage à la couche présentation
public sealed class GetUserHandler(IHttpContextAccessor http) { }
// ✅ Passer les infos via la query (userId dans la query)

// ❌ DbContext directement dans un QueryHandler
public sealed class GetUsersHandler(AppDbContext db) { }
// ✅ IUserReadRepository ou IQueryable<UserProjection>

// ❌ IServiceProvider — service locator anti-pattern
public sealed class SomeHandler(IServiceProvider services) { }
// ✅ Injecter les dépendances explicitement

// ❌ Autre ICommandHandler ou IQueryHandler injecté
public sealed class CreateOrderHandler(IQueryHandler<GetUserQuery, UserDto> getUserHandler) { }
// ✅ Injecter le repository/service directement

// ❌ IPipelineBehavior injecté dans un handler
// ❌ IRequestHandler<TRequest, TResponse> (interface MediatR interne)
```

## Règle de testabilité

Tout handler doit être testable avec :
```csharp
var handler = new MyHandler(mockDep1, mockDep2);
var result = await handler.Handle(command, CancellationToken.None);
```

**Pas de conteneur DI, pas de WebApplicationFactory, pas de base de données.**

Si un handler ne peut pas être testé ainsi → ses dépendances sont trop couplées → refactor.

## Couplage inter-handlers

```csharp
// ❌ Un handler appelle un autre handler directement
public async ValueTask<Result<InvoiceDto>> Handle(CreateInvoiceCommand cmd, ...)
{
    var order = await _orderHandler.Handle(new GetOrderQuery(cmd.OrderId), ct); // ❌
}
// ✅ Le handler accède directement au repository
var order = await _orderRepo.FindAsync(cmd.OrderId, ct);

// ❌ Un handler envoie une commande via IMediator
await _mediator.Send(new SendEmailCommand(user.Email)); // ❌ dans Handle()
// ✅ Le handler publie un DomainEvent et laisse un handler dédié agir
await _events.PublishAsync(new OrderCreatedEvent(orderId), ct);
```

## Communication entre bounded contexts

```csharp
// ✅ Via DomainEvents + handlers dédiés par BC
// ✅ Via une interface d'anti-corruption layer (IOrderingService depuis le BC Payment)
// ❌ Via IMediator partagé entre deux BCs dans le même process (couplage temporel)
```

## Signaux d'alerte (code review triggers)

Ces patterns déclenchent une review obligatoire :

```csharp
// 🔴 BLOQUANT
using MediatR; // dans un handler — sauf pour IRequest<T> marker

// 🔴 BLOQUANT
IMediator _mediator; // champ dans un handler

// 🟡 WARNING
IServiceProvider _services; // service locator

// 🟡 WARNING
HttpContext // ou IHttpContextAccessor dans un handler Application layer

// 🟡 WARNING
DbContext // dans un QueryHandler (devrait être un IReadRepository)
```
