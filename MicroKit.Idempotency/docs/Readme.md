# MicroKit.Idempotency

Request deduplication for .NET 10. A full state machine, SHA-256 payload drift detection, and an EF Core store that deliberately never calls `SaveChangesAsync` — so idempotency records commit atomically with your business operation.

---

## What makes this production-grade

**A state machine, not a flag.** `IdempotencyStatus` has four values: `Processing`, `Completed`, `Failed`, `Cancelled`. `IdempotencyBehavior` routes each existing state differently:
- `Processing` → throw `IdempotencyProgressingException` (client should retry later with back-off)
- `Completed` → deserialize and return the cached response immediately, handler never called
- `Failed` or `Cancelled` → delete the record and execute the handler as a fresh request

A naive implementation checks only for a completed record. MicroKit handles the concurrent-duplicate case and the retry-after-failure case correctly.

**SHA-256 payload drift detection.** When `IdempotencyOptions.VerifyRequestHashes = true`, the behavior computes a SHA-256 hash of the request object via `IRequestHasher.ComputeHash<T>` (serialize → UTF-8 bytes → `SHA256.HashData` → Base64) and stores it alongside the record. On a duplicate call with the same key but different payload, it throws `IdempotencyConflictException` before the handler is invoked. This catches accidental parameter mutation and protocol bugs in callers.

**`OperationCanceledException` gets its own terminal status.** Cancellation is recorded as `IdempotencyStatus.Cancelled`, not `Failed`. On the next call with the same key, the cancelled record is deleted and the handler runs fresh — consistent with HTTP 499 semantics.

**EF Core store stages, never saves.** `EfCoreIdempotencyStore` calls `FindAsync`, `AddAsync`, and updates entity properties in the ChangeTracker. It never calls `SaveChangesAsync`. The caller's `IUnitOfWork.SaveChangesAsync` commits the idempotency record and the business aggregate update in a single round trip. The two writes are either both committed or both rolled back.

**In-line expiry check on read.** `GetAsync` checks `ExpiresAtUtc` on the fetched record before returning it. If the record has expired, it is deleted from the ChangeTracker and `null` is returned — the caller retries as a new request. There is no separate background cleanup required for correctness (though `IdempotencyCleanupWorker` still runs for housekeeping).

**`RenewExpirationOnComplete` extends the TTL post-completion.** When this option is set, `CompleteAsync` resets `ExpiresAtUtc` to `UtcNow + DefaultTtl`. This gives clients a safe window to retry their original request after receiving the response, without the record expiring between their retry and the application's next read.

**Tenant-scoped keys.** `IdempotencyBehavior` optionally injects `ITenantContext`. When present and resolved, the tenant ID is stored with the record — the same key from two tenants creates two independent records.

---

## Installation

```shell
# Contracts — IIdempotencyStore, IIdempotentRequest<TResponse>, IRequestHasher
dotnet add package MicroKit.Idempotency.Abstractions

# Core: IdempotencyProvider, SHA-256 hasher, cleanup worker, AddMicroKitIdempotency extension
dotnet add package MicroKit.Idempotency.Core

# EF Core store — stages changes, never calls SaveChangesAsync
dotnet add package MicroKit.Idempotency.EFCore

# Redis store
dotnet add package MicroKit.Idempotency.Redis

# MediatR pipeline behavior — the state machine
dotnet add package MicroKit.Idempotency.MediatR
```

---

## Usage

### Mark a command as idempotent

```csharp
using MicroKit.Idempotency.Abstractions.Contracts;
using MicroKit.Cqrs.Abstractions.Commands;
using MicroKit.Domain.Primitives;

public record CreatePaymentCommand(
    Guid PaymentId,
    decimal Amount,
    string Currency) : ICommand<Result<Guid>>, IIdempotentRequest<Result<Guid>>
{
    // Supplied by the caller — e.g. mapped from the X-Idempotency-Key HTTP header
    public required string IdempotencyKey { get; init; }

    // Per-request TTL. When null, IdempotencyOptions.DefaultExpiration is used.
    public TimeSpan? IdempotencyExpiration => TimeSpan.FromDays(7);
}
```

The handler implementation requires no changes. The pipeline behavior handles all idempotency logic before and after the handler runs.

### Map the idempotency key from an HTTP header

```csharp
app.MapPost("/payments", async (
    CreatePaymentRequest req,
    HttpContext http,
    ICommandBus bus,
    CancellationToken ct) =>
{
    var key = http.Request.Headers["X-Idempotency-Key"].FirstOrDefault()
              ?? Guid.NewGuid().ToString();

    var cmd = new CreatePaymentCommand(req.PaymentId, req.Amount, req.Currency)
    {
        IdempotencyKey = key
    };

    try
    {
        var result = await bus.SendAsync<Result<Guid>>(cmd, ct);
        return result.IsSuccess
            ? Results.Created($"/payments/{result.Value}", null)
            : Results.UnprocessableEntity(result.Error.Message);
    }
    catch (IdempotencyProgressingException)
    {
        // Another worker is processing the same key right now
        return Results.Conflict("This request is already being processed. Retry after a short delay.");
    }
    catch (IdempotencyConflictException ex)
    {
        // Same key, different payload — caller bug
        return Results.BadRequest($"Idempotency conflict for key '{ex.Key}': payload has changed.");
    }
});
```

### Handler — unchanged

The command handler has no idempotency awareness. The behavior intercepts before and after.

```csharp
public sealed class CreatePaymentHandler : ICommandHandler<CreatePaymentCommand, Result<Guid>>
{
    private readonly IRepository<Payment> _payments;
    private readonly IUnitOfWork _uow;

    public CreatePaymentHandler(IRepository<Payment> payments, IUnitOfWork uow) =>
        (_payments, _uow) = (payments, uow);

    public async Task<Result<Guid>> HandleAsync(CreatePaymentCommand cmd, CancellationToken ct)
    {
        var payment = Payment.Create(cmd.PaymentId, cmd.Amount, cmd.Currency);
        await _payments.AddAsync(payment, ct);
        await _uow.SaveChangesAsync(ct);   // commits payment row AND idempotency record atomically
        return Result.Success(payment.Id);
    }
}
```

### State machine flow

```
First call (key = "pay-abc123"):
  1. GetAsync("pay-abc123") → null
  2. CreateAsync(state: Processing, ttl: 7 days)
  3. Execute handler → Result.Success(paymentId)
  4. CompleteAsync("pay-abc123", serialized response, Completed)
  5. SaveChangesAsync → payment row + idempotency record committed atomically

Duplicate call (same key, same payload):
  1. GetAsync("pay-abc123") → state: Completed
  2. VerifyHash (if enabled) → hashes match
  3. Return Deserialize<Result<Guid>>(state.Response) immediately
  → Handler never called

Concurrent duplicate (key in Processing):
  1. GetAsync("pay-abc123") → state: Processing
  2. Throw IdempotencyProgressingException → HTTP 409

Payload drift (same key, different payload):
  1. GetAsync("pay-abc123") → state: Completed
  2. VerifyHash → hash mismatch
  3. Throw IdempotencyConflictException → HTTP 400

Retry after failure:
  1. GetAsync("pay-abc123") → state: Failed
  2. DeleteAsync("pay-abc123")
  3. Re-run ProcessNewRequest → clean slate
```

### Custom request hasher normalizer

When the request contains timestamps or request IDs that should not affect the hash:

```csharp
public class CreatePaymentRequestHasher : IRequestHasher
{
    private readonly IRequestHasher _inner;

    public CreatePaymentRequestHasher(IRequestHasher inner) => _inner = inner;

    public string ComputeHash<T>(T request) =>
        request is CreatePaymentCommand cmd
            ? _inner.ComputeHash(cmd, r => $"{r.PaymentId}:{r.Amount}:{r.Currency}")
            : _inner.ComputeHash(request);

    public string ComputeHash<T>(T request, Func<T, string> normalizer) =>
        _inner.ComputeHash(request, normalizer);
}
```

---

## Configuration

```csharp
using MicroKit.Idempotency.Core;
using MicroKit.Idempotency.EFCore;
using MicroKit.Idempotency.MediatR;

builder.Services
    .AddMicroKit()
    .AddMicroKitIdempotency(idempotency =>
    {
        // Persist in the application's own DbContext — atomic with business writes
        idempotency.UseEFcore<AppDbContext>(options =>
        {
            options.RenewExpirationOnComplete = true;
            options.DefaultTtl = TimeSpan.FromDays(7);
        });

        // Wire the state machine into the MediatR pipeline
        idempotency.UseMediatRPipeline();
    });
```

### Global options

Bound from `MicroKit:Idempotency`:

```json
{
  "MicroKit": {
    "Idempotency": {
      "VerifyRequestHashes": true,
      "DefaultExpiration": "7.00:00:00",
      "EnableLogging": true
    }
  }
}
```

### Add the idempotency table to your DbContext

```csharp
using MicroKit.Idempotency.EFCore.Extensions;

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ApplyIdempotencyConfiguration();   // adds IdempotencyRecords table
}
```

---

## Key types

| Type | Package | Role |
|---|---|---|
| `IIdempotentRequest<TResponse>` | Abstractions | Marker — carries `IdempotencyKey` and `IdempotencyExpiration` |
| `IIdempotencyStore` | Abstractions | Persistence contract: GetAsync, CreateAsync, CompleteAsync, FailAsync, DeleteAsync, RenewExpirationAsync |
| `IdempotencyState` | Abstractions | Immutable record: Key, TenantId, Status, Response, RequestHash, timestamps |
| `IdempotencyStatus` | Abstractions | `Processing`, `Completed`, `Failed`, `Cancelled` |
| `IRequestHasher` | Abstractions | SHA-256 hash of request; supports custom normalizer |
| `IdempotencyBehavior<TRequest, TResponse>` | MediatR | Full state machine as MediatR `IPipelineBehavior` |
| `EfCoreIdempotencyStore<TContext>` | EFCore | Stages in ChangeTracker; never calls SaveChangesAsync |
| `IdempotencyProgressingException` | Core | Thrown when key is in Processing state |
| `IdempotencyConflictException` | Core | Thrown when payload hash differs from stored hash |
| `IdempotencyCleanupWorker` | Core | Background cleanup of expired records |

---

## Package dependency graph

```
MicroKit.Idempotency.Abstractions
    (no NuGet dependencies)

MicroKit.Idempotency.Core
    MicroKit.Idempotency.Abstractions
    MicroKit.Abstractions
    Microsoft.Extensions.Hosting

MicroKit.Idempotency.EFCore
    MicroKit.Idempotency.Abstractions
    MicroKit.Idempotency.Core
    Microsoft.EntityFrameworkCore

MicroKit.Idempotency.Redis
    MicroKit.Idempotency.Abstractions
    MicroKit.Idempotency.Core
    StackExchange.Redis

MicroKit.Idempotency.MediatR
    MicroKit.Idempotency.Abstractions
    MicroKit.Idempotency.Core
    MicroKit.MultiTenancy.Abstractions   (optional — for tenant-scoped keys)
    MediatR
```
