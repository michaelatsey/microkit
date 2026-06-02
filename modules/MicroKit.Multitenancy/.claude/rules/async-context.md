# Rule: Async Context — MicroKit.Multitenancy

## Always active for any file touching ITenantContextAccessor or AsyncLocal.

## Fundamental model

`AsyncLocal<T>` flows with the `ExecutionContext` in async/await chains automatically.
It does NOT propagate to new threads created via `Thread.Start()`, `ThreadPool.QueueUserWorkItem`,
or `Task.Run()` with a captured closure that reads the `AsyncLocal` directly.

### ✅ Linear async/await — no action needed
```csharp
// ExecutionContext flows automatically through await
var tenant = _accessor.GetTenant(); // Tenant A
await ProcessOrderAsync(orderId, ct); // still sees Tenant A — correct
```

### ✅ Background work — must use CreateScope
```csharp
// ✅ Capture before spawning background work
var currentTenant = _accessor.GetTenant();
_ = Task.Run(async () =>
{
    using var scope = _accessor.CreateScope(currentTenant!);
    await ProcessOrderInBackgroundAsync(orderId, ct).ConfigureAwait(false);
});
```

### ❌ Reading accessor directly in Task.Run — context lost
```csharp
// ❌ AsyncLocal value is NOT inherited by new ThreadPool threads
_ = Task.Run(async () =>
{
    var tenant = _accessor.GetTenant(); // may be null — not propagated
});
```

## Registration rules

```
ITenantContextAccessor MUST be registered as Scoped (never Singleton, never Transient)
  Scoped → one instance per DI scope → one AsyncLocal stack per request/unit-of-work
  Singleton → shared across requests → context leaks between requests
  Transient → separate instance → can't share context within same request
```

```csharp
// ✅ Correct registration in DI
services.AddScoped<ITenantContextAccessor, AsyncLocalTenantContextAccessor>();

// ❌ Singleton registration — MKT003 analyzer error
services.AddSingleton<ITenantContextAccessor, AsyncLocalTenantContextAccessor>(); // ❌
```

## IDisposable scope (mandatory implementation)

Every `ITenantContextAccessor` implementation must support `CreateScope(ITenantInfo)` returning `IDisposable`.
The scope restores the previous value on `Dispose()`, enabling nested tenant contexts.

```csharp
// ✅ Nested scope — correctly restores outer tenant
using (_accessor.CreateScope(tenantB))
{
    // sees TenantB
    using (_accessor.CreateScope(tenantA))
    {
        // sees TenantA
    }
    // restored to TenantB
}
// restored to original tenant
```

## Parallel.ForEachAsync pattern

```csharp
// ✅ Capture once, set per-iteration
var outerTenant = _accessor.GetTenant();
await Parallel.ForEachAsync(items, ct, async (item, token) =>
{
    using var scope = _accessor.CreateScope(outerTenant!);
    await ProcessItemAsync(item, token).ConfigureAwait(false);
});
```

## Rules (non-negotiable)

```
🔴 ITenantContextAccessor injected in a Singleton service → MKT003 analyzer error
🔴 Reading ITenantContextAccessor directly inside Task.Run/ThreadPool without CreateScope
🔴 Scope.Dispose() must restore the PREVIOUS value (not null) — nested scopes must work
🔴 AsyncLocal<T> must NOT be a static field — must be an instance field of the accessor
🟡 Tests must verify isolation between concurrent requests (parallel Task.WhenAll with different tenants)
```
