# Rule: Naming — MicroKit.MediatR

## Applicabilité
Toujours actif pour tout fichier `.cs` du module. Complète la table de nommage de
`.claude/rules/csharp-style.md` et le registre canonique `.claude-context/standards/naming-conventions.md`.

## Contrats CQRS

| Type | Convention | Exemple |
|---|---|---|
| Command | `{Verb}{Entity}Command` | `CreateOrderCommand`, `DeleteUserCommand` |
| Query | `Get{Entity}[By{Discriminant}]Query` | `GetUserByIdQuery`, `GetOrdersByStatusQuery` |
| Stream Query | `Stream{Entities}Query` ou `Get{Entities}FeedQuery` | `StreamProductsQuery`, `GetProductsFeedQuery` |
| CommandHandler | `{Verb}{Entity}Handler` | `CreateOrderHandler` |
| QueryHandler | `Get{Entity}Handler` | `GetUserByIdHandler` |
| StreamQueryHandler | `{Verb}{Entities}Handler` | `GetProductsFeedHandler` |

## DomainEvents & Notifications

| Type | Convention | Exemple |
|---|---|---|
| DomainEvent | `{Entity}{FactPast}Event` | `OrderShippedEvent`, `UserRegisteredEvent` |
| Notification | `{Entity}{FactPast}Notification` | `OrderShippedNotification` |
| EventHandler | `{HandlerAction}Handler` | `SendShippingConfirmationHandler` |

> Les noms de DomainEvent sont **toujours** au passé : le fait a déjà eu lieu.

## Behaviors & Markers

| Type | Convention | Exemple |
|---|---|---|
| Behavior | `{Concern}Behavior` | `ValidationBehavior`, `RetryBehavior` |
| Marker (request) | `I{Concern}Request` | `IRetryableRequest`, `IAuthorizedRequest` |
| Marker (command-only) | `I{Concern}Command` | `IIdempotentCommand` |
| Marker (query-only) | `I{Concern}Query` | `ICacheableQuery` |
| Base | `{Concern}Base` | `BehaviorBase` |
| Order registry | `PipelineOrder` (static class de `const int`) | `PipelineOrder.Validation` |

> Le suffixe du marker indique le scope autorisé : `*Command` ⇒ commands uniquement,
> `*Query` ⇒ queries uniquement, `*Request` ⇒ les deux. Voir `.claude/rules/pipeline-behaviors.md`.

## Méthodes de dispatch & DI

- Extensions de dispatch sur `IMediator` : `SendCommandAsync`, `SendQueryAsync`, `StreamQueryAsync`
- Entrée DI principale : `AddMicroKitMediatR(this IServiceCollection, Action<MediatRBuilder>)`
- Activation d'un behavior en fluent : `Add{Concern}Behavior()` — `AddValidationBehavior()`, `AddRetryBehavior()`
- Dispatcher de domaine : `IDomainEventDispatcher.PublishAsync(...)`

## Propriétés de log

Les propriétés structurées émises par le `LoggingBehavior` utilisent **uniquement** les
constantes de `LogPropertyNames` (MicroKit.Logging.Abstractions). Pour MediatR, la propriété
canonique est `LogPropertyNames.CommandName`. Ne jamais coder en dur `"CommandName"` ou un
variant comme `"command_name"`.

## Fichiers

- Un type par fichier ; nom de fichier = nom du type (`CreateOrderCommand.cs`)
- Handlers dans un sous-dossier `Handlers/` à côté de leur contrat côté consommateur
- Classes partielles : `{TypeName}.{Concern}.cs` (`MediatRBuilder.Registration.cs`)
