# Rule: Resolution Pipeline — MicroKit.Multitenancy

## Always active for any file touching ITenantResolutionStrategy or ITenantResolver.

## Pipeline contract

### ITenantResolutionStrategy — single strategy
```csharp
// ✅ Returns Result<TenantId> — never throws, never returns null
public interface ITenantResolutionStrategy
{
    /// <summary>Priority order — lower value runs first.</summary>
    int Order { get; }

    /// <summary>
    /// Attempts to resolve a TenantId from the current context.
    /// Returns <see cref="Result{TenantId}.Failure"/> if this strategy cannot resolve.
    /// </summary>
    ValueTask<Result<TenantId>> TryResolveAsync(CancellationToken ct = default);
}
```

### ITenantResolver — orchestrates strategies
```csharp
// ✅ Returns Result<ITenantInfo> — never throws
public interface ITenantResolver
{
    /// <summary>
    /// Resolves the current tenant by iterating registered strategies in Order.
    /// Short-circuits on the first successful resolution.
    /// Returns <see cref="Result{ITenantInfo}.Failure"/> if no strategy resolves.
    /// </summary>
    ValueTask<Result<ITenantInfo>> ResolveAsync(CancellationToken ct = default);
}
```

## Ordering and short-circuit

Strategies are ordered by `ITenantResolutionStrategy.Order` (ascending).
The pipeline short-circuits on the **first** successful `Result<TenantId>`.

```csharp
// ✅ Strategy ordering example
// Order 1: HeaderTenantResolutionStrategy   — X-Tenant-Id header
// Order 2: RouteDataTenantResolutionStrategy — {tenantId} route parameter
// Order 3: SubdomainTenantResolutionStrategy — {tenant}.app.example.com
// Order 4: ClaimsTenantResolutionStrategy   — JWT claim
// Order 5: HostTenantResolutionStrategy     — full host name mapping
```

## No-throw contract

```csharp
// ✅ Strategy returns failure — never throws on unresolvable
public async ValueTask<Result<TenantId>> TryResolveAsync(CancellationToken ct = default)
{
    var header = _httpContextAccessor.HttpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();
    if (string.IsNullOrEmpty(header))
        return Result<TenantId>.Failure(MultitenancyErrors.TenantNotFound);

    if (!Guid.TryParse(header, out var id))
        return Result<TenantId>.Failure(MultitenancyErrors.InvalidTenantId);

    return Result<TenantId>.Success(new TenantId(id));
}

// ❌ Throwing on unresolvable — breaks the pipeline contract
public async ValueTask<Result<TenantId>> TryResolveAsync(CancellationToken ct = default)
{
    var header = _http.HttpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();
    if (header is null) throw new TenantResolutionException("header missing"); // ❌
}
```

## Middleware integration

```csharp
// ✅ Middleware resolves tenant once per request and sets context
public sealed class TenantResolutionMiddleware(RequestDelegate next, ITenantResolver resolver,
    ITenantContextAccessor accessor)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var result = await resolver.ResolveAsync(context.RequestAborted).ConfigureAwait(false);
        if (result.IsSuccess)
            accessor.SetTenant(result.Value);

        await next(context).ConfigureAwait(false);
    }
}
```

## Rules (non-negotiable)

```
🔴 ITenantResolutionStrategy.TryResolveAsync MUST return Result<TenantId>.Failure on unresolvable
🔴 Strategy MUST NOT throw — any exception propagates to caller and breaks the pipeline
🔴 Strategy.Order must be unique per registered strategy set (duplicates log a warning)
🔴 ITenantResolver.ResolveAsync returns Result.Failure if NO strategy resolved — never null
🟡 Middleware MUST NOT reject requests on tenant resolution failure by default (configurable)
🟡 Failed resolution MUST be observable (ILogger warning, not silent null)
```
