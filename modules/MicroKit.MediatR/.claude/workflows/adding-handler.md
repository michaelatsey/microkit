# Workflow: Adding a Handler

Step-by-step guide for adding a new CQRS handler (command, query, stream, or event handler).

## When to Use

When you need a new business operation: a state change (command), a read (query), a large read
(stream), or a reaction to a domain fact (event handler).

## Steps

### 1. Decide the Contract

Run the decision in `.claude/skills/cqrs-patterns/SKILL.md`:
- Changes state → `ICommand` / `ICommand<Result<TId>>`
- Reads state → `IQuery<Result<TDto>>` / `IQuery<TDto>`
- Large read → `IStreamQuery<TDto>`
- Reacts to a fact → domain event + notification + handler (use `/new-domain-event`)

If unsure, ask the `architect` agent.

### 2. Plan (for non-trivial work)

Use the `implementer` agent to produce a plan and approve it before code.

### 3. Scaffold

```
/new-handler <Name> --type <command|query|event|stream> [--result] [--input "..."] [--output Type] [--behaviors validation,cache,retry,idempotent,auth]
```

This generates the contract, optional validator, handler skeleton, and a test skeleton, and shows
the DI registration.

### 4. Apply the Markers

Add opt-in markers per the behaviors you need (`IIdempotentCommand`, `ICacheableQuery`,
`IRetryableRequest`, `IAuthorizedRequest`). Implement their config properties (`IdempotencyKey`,
`CacheKey`/`Expiry`, `MaxRetries`, `RequiredPolicies`).

### 5. Implement the Handler

- `sealed class` + primary constructor
- `ValueTask<T>` return, `CancellationToken ct = default` last
- `ConfigureAwait(false)` on every await
- Inject repositories/services directly — never `IMediator`
- Publish domain events **after** persistence, via `IDomainEventDispatcher`

### 6. Register in DI

```csharp
services.AddTransient<ICommandHandler<CreateOrderCommand, Result<OrderId>>, CreateOrderHandler>();
services.AddTransient<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
```

### 7. Tests

```
/new-handler-tests <HandlerName> --type <command|query|event|stream>
```

Cover the mandatory matrix (happy, not-found, validation, cancellation, event published, no
side-effect on failure). Shouldly + NSubstitute only.

### 8. Verify

```bash
dotnet build modules/MicroKit.MediatR/MicroKit.MediatR.slnx -c Debug
dotnet test  modules/MicroKit.MediatR/tests/MicroKit.MediatR.UnitTests/ --no-build
```
