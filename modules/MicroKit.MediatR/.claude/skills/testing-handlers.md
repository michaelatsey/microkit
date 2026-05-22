# Skill: Testing Handlers & Behaviors

## Quand activer ce skill
- Écriture de tests pour un CommandHandler, QueryHandler, DomainEventHandler
- Tests de behaviors (unitaires + pipeline complet)
- Debugging d'un test qui ne se comporte pas comme attendu
- Choix entre test unitaire handler vs test intégration pipeline

## Philosophie de test

```
Niveau 1 — Handler isolé (unitaire)
  → Harness + mocks NSubstitute
  → Rapide, déterministe, pas d'infrastructure
  → Couvre : logique métier, retours Result, domain events publiés

Niveau 2 — Pipeline + Handler (intégration légère)
  → FakeMediator (conteneur DI in-memory)
  → Vérifie que les behaviors s'appliquent correctement
  → Couvre : validation, authorization, caching, retry

Niveau 3 — E2E avec infrastructure réelle
  → WebApplicationFactory + DB in-memory
  → Couvre : enregistrement DI, mapping HTTP, persistance
  → Lent — à limiter aux scénarios critiques
```

**Règle :** 80% niveau 1, 15% niveau 2, 5% niveau 3.

## Harness par type de handler

### CommandHandlerTestHarness
```
Usage : tester un ICommandHandler en isolation
Fournit :
  - SendAsync(command) → TResponse
  - AssertEventPublished<TEvent>()
  - AssertNoEventsPublished()
  - AssertNoBehaviorErrors()
  - CapturedEvents → IReadOnlyList<IEvent>
```

### QueryHandlerTestHarness
```
Usage : tester un IQueryHandler en isolation
Fournit :
  - QueryAsync(query) → TResponse
  - AssertNoCacheInteraction() (vérifie que le handler ne gère pas lui-même le cache)
```

### DomainEventTestHarness
```
Usage : tester un IDomainEventHandler en isolation
Fournit :
  - HandleAsync(notification) → Task
  - Pas de retour de valeur (les event handlers ne retournent rien)
```

### FakeMediator (pipeline complet)
```
Usage : tester avec le pipeline de behaviors actif
Fournit :
  - SendCommandAsync<TResult>(command)
  - SendQueryAsync<TResult>(query)
  - StreamQueryAsync<TItem>(query) → IAsyncEnumerable
  - BuildWith(Action<IServiceCollection>) → configuration custom
```

## Patterns de mocking NSubstitute

### Repository
```csharp
var repo = Substitute.For<IUserRepository>();

// Retour nominal
repo.FindAsync(userId, Arg.Any<CancellationToken>()).Returns(user);

// Retour null (not found)
repo.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);

// Throw exception (infrastructure failure)
repo.SaveAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
    .Throws(new DbUpdateException("Connection lost"));

// Vérifier l'appel
await repo.Received(1).SaveAsync(
    Arg.Is<User>(u => u.Id == expectedId),
    Arg.Any<CancellationToken>());
```

### DomainEventDispatcher
```csharp
var events = Substitute.For<IDomainEventDispatcher>();

// Vérifier qu'un event a été publié avec les bonnes données
await events.Received(1).PublishAsync(
    Arg.Is<OrderCreatedEvent>(e => e.OrderId == expectedOrderId),
    Arg.Any<CancellationToken>());

// Vérifier qu'aucun event n'a été publié
await events.DidNotReceive().PublishAsync(
    Arg.Any<IEvent>(),
    Arg.Any<CancellationToken>());
```

## Assertions FluentAssertions + Result

```csharp
// Success path
result.Should().BeSuccess();
result.Should().BeSuccess().WithValue(v => v.Id == expectedId);
result.Should().BeSuccess().WithValue(v => v != null);

// Failure path
result.Should().BeFailure();
result.Should().BeFailure().WithError<UserNotFoundError>();
result.Should().BeFailure().WithError<UserNotFoundError>()
      .Which.UserId.Should().Be(expectedUserId);
result.Should().BeFailure().WithErrorCode("USER.NOT_FOUND");

// ErrorCollection (validation)
result.Should().BeFailure().WithError<ErrorCollection>()
      .Which.Errors.Should().HaveCount(3);
```

## Test du CancellationToken

```csharp
[Fact]
public async Task Handle_WhenCancelled_ThrowsOrReturnsGracefully()
{
    // Arrange
    using var cts = new CancellationTokenSource();
    var repo = Substitute.For<IUserRepository>();
    repo.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
        .Returns(async ci =>
        {
            await Task.Delay(100, ci.Arg<CancellationToken>()); // simule latence
            return (User?)null;
        });

    var handler = new GetUserByIdHandler(repo);
    cts.Cancel(); // annuler avant l'appel

    // Act & Assert
    await Assert.ThrowsAsync<OperationCanceledException>(
        () => handler.Handle(new GetUserByIdQuery(Guid.NewGuid()), cts.Token).AsTask());
}
```

## Test du pipeline complet (intégration légère)

```csharp
[Fact]
public async Task Pipeline_BlocksUnauthorizedRequest()
{
    // Arrange — FakeMediator avec AuthorizationBehavior
    var mediator = FakeMediator.BuildWith(services =>
    {
        services.AddMicroKitMediatR(cfg => cfg.AddAuthorizationBehavior());
        services.AddSingleton<IAuthorizationService>(
            FakeAuthorizationService.AlwaysDeny()); // always returns Forbidden
        services.AddTransient<ICommandHandler<DeleteUserCommand, Result<Unit>>,
                              DeleteUserHandler>();
    });

    // Act
    var result = await mediator.SendCommandAsync(new DeleteUserCommand(Guid.NewGuid()));

    // Assert — le behavior a court-circuité avant le handler
    result.Should().BeFailure().WithError<UnauthorizedError>();
}
```

## Fakers et builders de données de test

```csharp
// ✅ Builder pattern pour les commandes complexes
public sealed class CreateOrderCommandBuilder
{
    private Guid _userId = Guid.NewGuid();
    private List<OrderItem> _items = [new("SKU-001", 1)];

    public CreateOrderCommandBuilder WithUserId(Guid userId)
        { _userId = userId; return this; }

    public CreateOrderCommandBuilder WithEmptyCart()
        { _items = []; return this; }

    public CreateOrderCommand Build()
        => new(_userId, [.. _items]);
}

// Usage
var command = new CreateOrderCommandBuilder()
    .WithUserId(existingUserId)
    .Build();
```

## Pièges courants

```csharp
// ❌ Tester la logique du behavior dans le test du handler
// Le handler ne sait pas qu'un ValidationBehavior existe → ne pas le simuler

// ❌ Assert sur les internals MediatR
// Ne pas vérifier comment MediatR dispatche — vérifier le résultat

// ❌ Un seul [Fact] avec 5 assertions sur des scénarios différents
// → un [Fact] = un scénario = une assertion principale

// ❌ Shared state entre tests (champs mutables au niveau class)
// → créer les mocks dans le constructeur ou dans chaque test
```
