# Standard: Resolution Strategy Contracts

## ITenantResolutionStrategy

```csharp
public interface ITenantResolutionStrategy
{
    /// <summary>
    /// Execution order. Lower value = higher priority.
    /// Strategies are tried in ascending Order.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Attempts to resolve a tenant identifier from the current execution context.
    /// Returns <see cref="Result{TenantId}.Failure"/> if this strategy cannot resolve.
    /// Never throws — all errors are represented as Result failures.
    /// </summary>
    ValueTask<Result<TenantId>> TryResolveAsync(CancellationToken ct = default);
}
```

## ITenantResolver

```csharp
public interface ITenantResolver
{
    /// <summary>
    /// Resolves the current tenant by iterating registered strategies in Order.
    /// Short-circuits on the first successful resolution, then looks up the tenant in the store.
    /// </summary>
    ValueTask<Result<ITenantInfo>> ResolveAsync(CancellationToken ct = default);
}
```

## ITenantStore

```csharp
public interface ITenantStore
{
    /// <summary>Finds a tenant by its identifier.</summary>
    ValueTask<Result<ITenantInfo>> FindAsync(TenantId tenantId, CancellationToken ct = default);

    /// <summary>Returns all registered tenants.</summary>
    ValueTask<IReadOnlyList<ITenantInfo>> ListAllAsync(CancellationToken ct = default);
}
```

## Built-in HTTP Strategies (AspNetCore)

| Strategy | Order | Source | Header/Claim/Route |
|----------|-------|--------|--------------------|
| `HeaderTenantResolutionStrategy` | 1 | HTTP Header | `X-Tenant-Id` |
| `RouteDataTenantResolutionStrategy` | 2 | Route parameter | `{tenantId}` |
| `SubdomainTenantResolutionStrategy` | 3 | Subdomain | `{tenant}.app.example.com` |
| `ClaimsTenantResolutionStrategy` | 4 | JWT Claim | `tenant_id` |
| `HostTenantResolutionStrategy` | 5 | Full host | hostname → TenantId mapping |

## Resolution pipeline error codes

Each error is a concrete `sealed record` inheriting from `Error`. Static instances are
exposed via `MultitenancyErrors` for convenient usage in strategy implementations.

```csharp
// Concrete error types (in MicroKit.Multitenancy.Abstractions/Errors/)
public sealed record TenantNotFoundError()
    : Error(ErrorCode.From("MULTITENANCY.TENANT.NOT_FOUND"), "Tenant not found.")
{ public override ErrorCategory Category => ErrorCategory.NotFound; }

public sealed record InvalidTenantIdError()
    : Error(ErrorCode.From("MULTITENANCY.TENANT.INVALID_ID"), "The provided tenant identifier is invalid.")
{ public override ErrorCategory Category => ErrorCategory.Validation; }

public sealed record TenantInactiveError()
    : Error(ErrorCode.From("MULTITENANCY.TENANT.INACTIVE"), "The tenant is inactive.")
{ public override ErrorCategory Category => ErrorCategory.Forbidden; }

public sealed record ResolutionFailedError()
    : Error(ErrorCode.From("MULTITENANCY.RESOLUTION.FAILED"), "No strategy could resolve the current tenant.")
{ public override ErrorCategory Category => ErrorCategory.NotFound; }

// Static accessor (use this in strategy implementations)
public static class MultitenancyErrors
{
    public static readonly Error TenantNotFound   = new TenantNotFoundError();
    public static readonly Error InvalidTenantId  = new InvalidTenantIdError();
    public static readonly Error TenantInactive   = new TenantInactiveError();
    public static readonly Error ResolutionFailed = new ResolutionFailedError();
}
```
