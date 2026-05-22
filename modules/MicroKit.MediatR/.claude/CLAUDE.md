# MicroKit.MediatR — Project Brain

## 🎯 Mission
Surcouche opinionée sur MediatR (Jimmy Bogard) apportant :
- CQRS strict avec `ICommand` / `IQuery` séparés et fortement typés
- Pipeline behaviors pré-câblés (validation, logging, retry, cache, idempotency, authorization)
- Intégration optionnelle avec `MicroKit.Result` (`Result<T>` ou `T` direct)
- Support `DomainEventNotification` pour le DDD
- Streaming via `IAsyncEnumerable<T>`
- DI-agnostique (MEDI + Autofac)
- Testabilité first-class avec helpers et fakes intégrés

## 🏛️ Relation avec MediatR

MicroKit.MediatR **ne remplace pas** MediatR — il le wrapping et l'enrichit.
```
MediatR (Jimmy Bogard)           ← moteur de dispatch sous-jacent
    └── MicroKit.MediatR         ← contrats typés + behaviors + conventions
            └── Ton application  ← handlers métier propres
```

**Ce que MicroKit ajoute :**
- Contrats CQRS fortement typés (`ICommand<TResult>`, `IQuery<TResult>`)
- Pipeline par défaut pré-configuré (ordre déterministe)
- `DomainEventNotification<TDomainEvent>` pour le DDD
- Extensions de test (`FakeMediator`, `CommandHandlerTestHarness`)
- Configuration fluente du pipeline custom

**Ce que MicroKit ne touche pas :**
- Le dispatcher MediatR interne (`IMediator`)
- La résolution des handlers via DI
- Les conventions de scan d'assemblies MediatR

## 📐 Modèle CQRS Strict

```
ICommand<TResult>           ← mute l'état, retourne TResult (ou Result<TResult>)
ICommand                    ← mute l'état, pas de valeur de retour
IQuery<TResult>             ← lit l'état, retourne TResult (ou Result<TResult>)
IStreamQuery<TResult>       ← lit l'état, retourne IAsyncEnumerable<TResult>
IEvent                      ← notification domaine, pas de réponse
IDomainEventNotification<T> ← wrapping d'un DomainEvent pour MediatR
```

### Règle fondamentale (CQS)
> **Une commande ne retourne pas de données métier. Une query ne mute pas l'état.**
> Exception tolérée : une commande peut retourner l'ID de la ressource créée.

## 🔄 Pipeline par Défaut (ordre déterministe)

```
Requête entrante
    │
    ▼
[1] LoggingBehavior          ← trace structurée entrée/sortie + durée
    │
    ▼
[2] AuthorizationBehavior    ← vérifie IAuthorizedRequest si implémenté
    │
    ▼
[3] ValidationBehavior       ← FluentValidation si IValidator<T> enregistré
    │
    ▼
[4] IdempotencyBehavior      ← déduplication si IIdempotentCommand implémenté
    │
    ▼
[5] CachingBehavior          ← cache si ICacheableQuery implémenté (queries only)
    │
    ▼
[6] RetryBehavior            ← Polly retry si IRetryableRequest implémenté
    │
    ▼
[7] Handler métier           ← ton code
    │
    ▼
Réponse
```

L'ordre est garanti par `PipelineOrder` enum. Chaque behavior est opt-in via interface marker.

## 🔌 Intégration MicroKit.Result (optionnelle)

Les handlers peuvent retourner `Result<T>` **ou** `T` directement — les deux sont supportés.

```csharp
// Style Result<T> (recommandé pour les opérations qui peuvent échouer)
public sealed class GetUserHandler : IQueryHandler<GetUserQuery, Result<UserDto>>
{
    public async ValueTask<Result<UserDto>> Handle(GetUserQuery query, CancellationToken ct)
        => await _repo.FindAsync(query.UserId, ct) is { } user
            ? Result.Success(user.ToDto())
            : Result.Failure<UserDto>(new UserNotFoundError(query.UserId));
}

// Style T direct (pour les cas simples ou sans échec prévisible)
public sealed class GetConfigHandler : IQueryHandler<GetConfigQuery, AppConfig>
{
    public async ValueTask<AppConfig> Handle(GetConfigQuery query, CancellationToken ct)
        => await _config.GetAsync(ct);
}
```

## 🧱 DomainEvent Pattern

```csharp
// 1. Event de domaine (pur, sans dépendance infrastructure)
public sealed record UserRegisteredEvent(Guid UserId, string Email, DateTimeOffset RegisteredAt);

// 2. Notification MediatR wrappée
public sealed class UserRegisteredNotification
    : DomainEventNotification<UserRegisteredEvent>
{
    public UserRegisteredNotification(UserRegisteredEvent domainEvent) : base(domainEvent) { }
}

// 3. Handler
public sealed class SendWelcomeEmailHandler
    : IDomainEventHandler<UserRegisteredEvent, UserRegisteredNotification>
{
    public async Task Handle(UserRegisteredNotification notification, CancellationToken ct)
        => await _emailService.SendWelcomeAsync(notification.DomainEvent.Email, ct);
}
```

## 📦 Packages Cibles
- `MicroKit.MediatR` — core (dépend de MediatR)
- `MicroKit.MediatR.Behaviors` — tous les behaviors out-of-the-box
- `MicroKit.MediatR.Testing` — fakes + harness de test (dépend de xUnit)
- `MicroKit.MediatR.DependencyInjection` — extensions MEDI + Autofac

## 🗂️ Structure Recommandée

```
src/
└── MicroKit.MediatR/
    ├── Abstractions/
    │   ├── ICommand.cs              ← ICommand, ICommand<TResult>
    │   ├── IQuery.cs                ← IQuery<TResult>, IStreamQuery<TResult>
    │   ├── ICommandHandler.cs
    │   ├── IQueryHandler.cs
    │   ├── IEvent.cs
    │   ├── IDomainEventNotification.cs
    │   └── IDomainEventHandler.cs
    ├── Behaviors/
    │   ├── Core/
    │   │   ├── PipelineOrder.cs
    │   │   └── BehaviorBase.cs
    │   ├── LoggingBehavior.cs
    │   ├── ValidationBehavior.cs
    │   ├── AuthorizationBehavior.cs
    │   ├── IdempotencyBehavior.cs
    │   ├── CachingBehavior.cs
    │   └── RetryBehavior.cs
    ├── Markers/
    │   ├── IAuthorizedRequest.cs
    │   ├── ICacheableQuery.cs
    │   ├── IIdempotentCommand.cs
    │   └── IRetryableRequest.cs
    ├── DomainEvents/
    │   ├── DomainEventNotification.cs
    │   └── IDomainEventDispatcher.cs
    ├── Pipeline/
    │   ├── PipelineConfigurator.cs
    │   └── PipelineConfiguratorExtensions.cs
    ├── Extensions/
    │   ├── MediatorExtensions.cs        ← SendCommand / SendQuery helpers
    │   └── ResultMediatorExtensions.cs  ← extensions spécifiques Result<T>
    ├── DependencyInjection/
    │   ├── ServiceCollectionExtensions.cs
    │   └── AutofacExtensions.cs
    └── Testing/
        ├── FakeMediator.cs
        ├── CommandHandlerTestHarness.cs
        ├── QueryHandlerTestHarness.cs
        └── DomainEventTestHarness.cs
```

## 🧪 Testing Philosophy

Chaque handler est **testable en isolation** sans MediatR réel :

```csharp
// Pattern recommandé — harness d'isolation
var harness = new CommandHandlerTestHarness<CreateOrderCommand, Result<OrderId>>(
    new CreateOrderHandler(mockRepo, mockEvents));

var result = await harness.SendAsync(new CreateOrderCommand(userId, items));

result.Should().BeSuccess().WithValue(v => v != Guid.Empty);
harness.AssertNoBehaviorErrors();
```

## 🔗 Références
- [MediatR GitHub](https://github.com/jbogard/MediatR)
- [CQRS — Martin Fowler](https://martinfowler.com/bliki/CQRS.html)
- [Domain Events — Udi Dahan](https://udidahan.com/2009/06/14/domain-events-salvation/)
- [Pipeline Behaviors — MediatR docs](https://github.com/jbogard/MediatR/wiki/Behaviors)
- MicroKit.Result — `.claude/CLAUDE.md` dans `microkit-result/`
