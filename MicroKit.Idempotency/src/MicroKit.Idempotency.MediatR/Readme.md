# MicroKit.Idempotency.MediatR

MediatR pipeline behavior that enforces end-to-end idempotency for commands implementing `IIdempotentRequest<TResponse>`. Intercepts the pipeline before the handler executes, checks the store for an existing record, and either returns the cached response (for duplicates) or processes the request and persists its result.

## When to use

Add this package alongside `MicroKit.Idempotency.EFCore` or `MicroKit.Idempotency.Redis` when you want automatic idempotency enforcement at the pipeline level without modifying individual command handlers.

## Installation

```
dotnet add package MicroKit.Idempotency.MediatR
```

## Key types

| Type | Description |
|---|---|
| `IdempotencyBehavior<TRequest, TResponse>` | MediatR `IPipelineBehavior` that enforces idempotency for requests implementing `IIdempotentRequest<TResponse>` |
| `DependencyInjection.UseMediatRPipeline()` | Registers `IdempotencyBehavior<,>` as transient via `MicroKitIdempotencyBuilder` |

## Behavior matrix

| Existing record status | Action |
|---|---|
| None | Create `Processing` record, execute handler, mark `Completed` with serialized response |
| `Completed` | Return deserialized cached response immediately; handler is not called |
| `Processing` | Throw `IdempotencyProgressingException` — a concurrent request is still running |
| `Failed` or `Cancelled` | Delete stale record, retry as a new request |

Optional request hash verification (`IdempotencyOptions.VerifyRequestHashes`) computes a hash of the request body and compares it against the stored hash. A mismatch throws `IdempotencyConflictException` to prevent clients from reusing an idempotency key with different payload content.

## Usage

```csharp
// Registration
services
    .AddMicroKitIdempotency(options =>
    {
        options.DefaultExpiration = TimeSpan.FromHours(24);
        options.VerifyRequestHashes = true;
        options.EnableLogging = true;
    })
    .UseEFcore<AppDbContext>()
    .UseMediatRPipeline();

// Command that opts into idempotency
public record CreatePaymentCommand(
    Guid OrderId,
    decimal Amount,
    string IdempotencyKey)
    : ICommand<PaymentResult>,
      IRequest<PaymentResult>,
      IIdempotentRequest<PaymentResult>
{
    string IIdempotentRequest.IdempotencyKey => IdempotencyKey;
    TimeSpan? IIdempotentRequest.IdempotencyExpiration => null; // use DefaultExpiration
}
```

## Dependencies

- `MicroKit.Idempotency.Core`
- `MicroKit.Idempotency.Abstractions`
- `MediatR`
