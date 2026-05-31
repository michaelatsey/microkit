# Template: Command Handler

Code template for a CQRS command + handler. Used by `/new-handler --type command`.
Replace all `{Placeholder}` values.

---

## File: `{Verb}{Entity}Command.cs`

```csharp
namespace {App}.Application.{Feature};

/// <summary>
/// {Description of the state change}.
/// </summary>
/// <param name="{Param}">{Description}.</param>
public sealed record {Verb}{Entity}Command({Inputs})
    : ICommand<Result<{ResultType}>>{Markers}
{
    // If IIdempotentCommand:
    /// <inheritdoc />
    public string IdempotencyKey => $"{verb}-{entity}:{/* deterministic key from inputs */}";
}
```

## File: `{Verb}{Entity}CommandValidator.cs` (when validation opt-in)

```csharp
using FluentValidation;

namespace {App}.Application.{Feature};

/// <summary>Validates <see cref="{Verb}{Entity}Command"/>.</summary>
public sealed class {Verb}{Entity}CommandValidator : AbstractValidator<{Verb}{Entity}Command>
{
    public {Verb}{Entity}CommandValidator()
    {
        RuleFor(x => x.{Param}).NotEmpty();
        // ...
    }
}
```

## File: `Handlers/{Verb}{Entity}Handler.cs`

```csharp
namespace {App}.Application.{Feature}.Handlers;

/// <summary>Handles <see cref="{Verb}{Entity}Command"/>.</summary>
public sealed class {Verb}{Entity}Handler(
    I{Entity}Repository repo,
    IDomainEventDispatcher events)
    : ICommandHandler<{Verb}{Entity}Command, Result<{ResultType}>>
{
    /// <inheritdoc />
    public async ValueTask<Result<{ResultType}>> Handle(
        {Verb}{Entity}Command command,
        CancellationToken ct = default)
    {
        // 1. Load / construct the aggregate (write side)
        // 2. Apply domain logic
        // 3. Persist
        var id = await repo.SaveAsync(/* aggregate */, ct).ConfigureAwait(false);

        // 4. Publish domain events AFTER persistence
        await events.PublishAsync(new {Entity}{FactPast}Event(id /* ... */), ct).ConfigureAwait(false);

        // 5. Return the result
        return Result.Success(id);
    }
}
```

## DI Registration

```csharp
services.AddTransient<ICommandHandler<{Verb}{Entity}Command, Result<{ResultType}>>, {Verb}{Entity}Handler>();
services.AddTransient<IValidator<{Verb}{Entity}Command>, {Verb}{Entity}CommandValidator>(); // if validation
```

## Rules Applied

- `sealed record` command, `sealed class` handler + primary constructor
- `ValueTask<Result<T>>`, `CancellationToken ct = default` last
- `ConfigureAwait(false)` on every await
- Domain events published **after** persistence via `IDomainEventDispatcher` — never `IMediator`
- Markers (`IIdempotentCommand`, `IAuthorizedRequest`, `IRetryableRequest`) added per requested behaviors
