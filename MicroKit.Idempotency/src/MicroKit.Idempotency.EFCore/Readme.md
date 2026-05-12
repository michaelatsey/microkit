# MicroKit.Idempotency.EFCore

EF Core-backed `IIdempotencyStore` that persists idempotency records in the application's own `DbContext`. Changes are staged on the EF Core change tracker without calling `SaveChangesAsync` — the caller's Unit of Work commits them atomically together with the business operation.

## When to use

Use this when your primary store is SQL Server or PostgreSQL and you want idempotency records co-located with business data in a single transaction. This eliminates distributed transaction requirements: either the business write and the idempotency record both commit, or neither does.

Use `MicroKit.Idempotency.Redis` when you prefer a separate fast key-value store for idempotency and can tolerate slightly weaker atomicity guarantees.

## Installation

```
dotnet add package MicroKit.Idempotency.EFCore
```

## Key types

| Type | Description |
|---|---|
| `EfCoreIdempotencyStore<TContext>` | `IIdempotencyStore` staged on the EF Core change tracker; no internal `SaveChanges` call |
| `EFCoreIdempotencyCleanupService<TContext>` | Batch `ExecuteDeleteAsync` on expired or completed records |
| `IdempotencyRecord` | EF Core entity mapped to `MicroKit_IdempotencyRecords`; includes `RowVersion` for optimistic locking |
| `IdempotencyRecordConfiguration` | Provider-aware `IEntityTypeConfiguration<IdempotencyRecord>` with indexes on `(Key, TenantId)`, `Status`, and `ExpiresAtUtc` |
| `EFCoreIdempotencyOptions` | `DefaultTtl`, `RenewExpirationOnComplete`, `Schema`, `TableName`, `EnableAutoCleanup`, `CleanupInterval` |
| `DependencyInjection.UseEFcore<TContext>()` | Registers the store and cleanup service via `MicroKitIdempotencyBuilder` |
| `IdempotencyModelBuilderExtensions.ApplyMicroKitIdempotencyConfigurations()` | Call from `OnModelCreating` to apply the entity configuration |

## Usage

```csharp
// 1. Registration
services
    .AddMicroKitIdempotency()
    .UseEFcore<AppDbContext>(options =>
    {
        options.DefaultTtl = TimeSpan.FromHours(48);
        options.EnableAutoCleanup = true;
        options.CleanupInterval = TimeSpan.FromHours(2);
    })
    .UseMediatRPipeline();

// 2. Apply entity configuration in DbContext
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ApplyMicroKitIdempotencyConfigurations(this);
}
```

The `MicroKit_IdempotencyRecords` table has a unique index on `(Key, TenantId)` to prevent duplicate creation under concurrent requests.

## Dependencies

- `MicroKit.Idempotency.Abstractions`
- `MicroKit.Idempotency.Core`
- `Microsoft.EntityFrameworkCore`
- `Microsoft.EntityFrameworkCore.Relational`
