# Rule: Resolution Pipeline — MicroKit.Tenancy

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
// Order 10: HeaderTenantResolutionStrategy      — X-Tenant-Id header
// Order 20: RouteDataTenantResolutionStrategy   — {tenantId} route parameter
// Order 30: SubdomainTenantResolutionStrategy   — {guid}.app.example.com (opt-in)
// Order 40: ClaimsTenantResolutionStrategy      — JWT claim
// Order 50: HostTenantResolutionStrategy        — full host name mapping (opt-in)
// Spacing of 10 allows inserting custom strategies between built-ins without renumbering.
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
// IMPORTANT: ITenantResolver and ITenantContextAccessor are Scoped. Middleware is effectively
// Singleton-lifetime. Use InvokeAsync method injection for scoped deps — NOT constructor injection.
// Constructor injection of Scoped services here would create a captive dependency (MKT003 violation).
public sealed partial class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ITenantResolver resolver,
        ITenantContextAccessor accessor,
        ILogger<TenantResolutionMiddleware> logger)
    {
        var result = await resolver.ResolveAsync(context.RequestAborted).ConfigureAwait(false);
        if (result.IsSuccess)
            accessor.SetTenant(result.Value);
        else
            LogTenantNotResolved(logger, context.Request.Path.ToString());

        await next(context).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning,
        Message = "Tenant could not be resolved for request '{Path}'. Proceeding without tenant context.")]
    private static partial void LogTenantNotResolved(ILogger logger, string path);
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
