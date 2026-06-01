# Standard: Naming Conventions

**Canonical type-name registry for MicroKit.MediatR.** This is the single source of truth for how
each kind of type is named. The `.claude/rules/naming.md` rule enforces it; `api-reviewer` checks it.

---

## CQRS Contracts

| Kind | Pattern | Examples |
|------|---------|----------|
| Command (no result) | `{Verb}{Entity}Command` | `DeleteUserCommand`, `ArchiveOrderCommand` |
| Command (with result) | `{Verb}{Entity}Command` | `CreateOrderCommand : ICommand<Result<OrderId>>` |
| Query | `Get{Entity}[By{Discriminant}]Query` | `GetUserByIdQuery`, `GetOrdersByStatusQuery` |
| Stream Query | `Stream{Entities}Query` / `Get{Entities}FeedQuery` | `StreamProductsQuery`, `GetProductsFeedQuery` |

## Handlers

| Kind | Pattern | Example |
|------|---------|---------|
| CommandHandler | `{Verb}{Entity}Handler` | `CreateOrderHandler` |
| QueryHandler | `Get{Entity}Handler` | `GetUserByIdHandler` |
| StreamQueryHandler | `{Verb}{Entities}Handler` | `GetProductsFeedHandler` |
| DomainEventHandler | `{HandlerAction}Handler` | `SendWelcomeEmailHandler` |

## Domain Events & Notifications

| Kind | Pattern | Example |
|------|---------|---------|
| DomainEvent | `{Entity}{FactPast}Event` | `OrderShippedEvent`, `UserRegisteredEvent` |
| Notification | `{Entity}{FactPast}Notification` | `OrderShippedNotification` |

DomainEvent names are always in the **past tense** — the fact has already occurred.

## Behaviors & Markers

| Kind | Pattern | Example | Scope implied |
|------|---------|---------|---------------|
| Behavior | `{Concern}Behavior` | `ValidationBehavior` | — |
| Marker (any request) | `I{Concern}Request` | `IRetryableRequest`, `IAuthorizedRequest` | commands + queries |
| Marker (command only) | `I{Concern}Command` | `IIdempotentCommand` | commands only |
| Marker (query only) | `I{Concern}Query` | `ICacheableQuery` | queries only |
| Order registry | `PipelineOrder` | `PipelineOrder.Validation` | — |
| Base class | `BehaviorBase` | — | — |

> The marker suffix encodes its scope: `*Command` ⇒ commands only, `*Query` ⇒ queries only,
> `*Request` ⇒ both. Behaviors enforce this with a guard.

## Dispatch & DI

| Kind | Pattern | Example |
|------|---------|---------|
| Dispatch extension | `Send{Command|Query}Async`, `StreamQueryAsync` | `mediator.SendCommandAsync(...)` |
| DI entry point | `AddMicroKitMediatR(...)` | — |
| Behavior activation | `Add{Concern}Behavior()` | `AddValidationBehavior()` |
| Event dispatcher | `IDomainEventDispatcher.PublishAsync` | — |

## Log Property

The only MediatR-owned canonical log property is `LogPropertyNames.CommandName` (string value
`"CommandName"`), emitted by the `LoggingBehavior`. Never hardcode the literal or a variant.

## Abbreviations

Allowed: `Id`, `Url`, `Http`, `Dto`. Everything else spelled out.
