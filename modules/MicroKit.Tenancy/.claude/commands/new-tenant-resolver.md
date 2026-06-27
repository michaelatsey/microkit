# Command: /new-tenant-resolver

Scaffold a custom `ITenantResolutionStrategy` implementation.

## Usage
```
/new-tenant-resolver [strategy-name] [source]
```

## Examples
```
/new-tenant-resolver MyCustom custom-header
/new-tenant-resolver Database database-lookup
```

## What gets generated

1. `src/MicroKit.Tenancy.AspNetCore/Strategies/{Name}TenantResolutionStrategy.cs`
   - `sealed class` implementing `ITenantResolutionStrategy`
   - `Order` property
   - `TryResolveAsync` returning `Result<TenantId>` (never throws)
   - XML documentation

2. Test: `tests/MicroKit.Tenancy.UnitTests/Strategies/{Name}TenantResolutionStrategyTests.cs`
   - Success case
   - Failure case (strategy returns Result.Failure)
   - No-throw case (exception in strategy returns Result.Failure)

## Template

```csharp
/// <summary>
/// Resolves the current tenant from [source].
/// </summary>
public sealed class {Name}TenantResolutionStrategy : ITenantResolutionStrategy
{
    /// <inheritdoc />
    public int Order => {n};

    /// <inheritdoc />
    public async ValueTask<Result<TenantId>> TryResolveAsync(CancellationToken ct = default)
    {
        // TODO: implement resolution logic
        // Return Result<TenantId>.Failure(MultitenancyErrors.TenantNotFound) if not resolvable
        // Never throw
    }
}
```
