# Architectural Decisions — MicroKit.Multitenancy

## ADR-001: AsyncLocal over IHttpContextAccessor for tenant context

**Status:** Accepted  
**Date:** 2026-06

**Context:**
Most .NET multitenancy libraries (Finbuckle.MultiTenant) bind tenant context to `IHttpContextAccessor`,
making them unusable in non-HTTP scenarios (background jobs, message consumers, gRPC, WebSockets).

**Decision:**
`ITenantContextAccessor` is backed by `AsyncLocal<ITenantInfo?>`, not `IHttpContextAccessor`.
ASP.NET Core middleware reads from HTTP context and writes to `ITenantContextAccessor`,
decoupling the resolution source from the context store.

**Consequences:**
- Core package has zero ASP.NET Core dependency
- Works in any host: minimal API, Worker, Console, gRPC, Azure Functions
- HTTP strategies live exclusively in AspNetCore package
- Tests for Core do not require `TestServer` or `WebApplicationFactory`

---

## ADR-002: Resolution pipeline returns Result<T>, never throws

**Status:** Accepted  
**Date:** 2026-06

**Context:**
A tenant not being resolvable is an expected runtime condition (anonymous endpoint, pre-auth
request), not an exceptional case. Throwing an exception on every unauthenticated request
would be noisy, expensive, and mis-use of the exception mechanism.

**Decision:**
`ITenantResolutionStrategy.TryResolveAsync` and `ITenantResolver.ResolveAsync` return
`Result<T>` from `MicroKit.Result`. Strategies catch all exceptions internally and return
`Result.Failure`. Middleware decides how to handle an unresolved tenant (reject or allow).

**Consequences:**
- No `try/catch` required in middleware — result pattern covers it
- Caller has explicit choice: reject request (401/404) or allow with null tenant
- Testable without exception handling setup

---

## ADR-003: EF Core Shared mode only in Phase 1

**Status:** Accepted  
**Date:** 2026-06

**Context:**
Three isolation modes exist: Shared (TenantId column), Schema (per-tenant schema),
Database (per-tenant connection string). Each adds significant complexity.

**Decision:**
Phase 1 implements Shared mode only:
- Global query filter on every `ITenantEntity`
- `SaveChanges` interceptor stamps `TenantId` on `Added` entities
- Row-level isolation, single database

Schema and Database modes are deferred to Phase 2.

**Consequences:**
- Simpler DbContext lifecycle — single context per scope
- Works with existing `MicroKit.Persistence` EF Core patterns
- Phase 2 will require per-tenant `IDbContextFactory<T>` or connection switching

---

## ADR-004: Analyzers are build-time only (netstandard2.0)

**Status:** Accepted  
**Date:** 2026-06

**Context:**
Roslyn analyzers must target `netstandard2.0` for cross-SDK compatibility.

**Decision:**
`MicroKit.Multitenancy.Analyzers` targets `netstandard2.0` with `IncludeBuildOutput=false`.
It has no dependency on other MicroKit packages at runtime.
It detects violations via symbol analysis (no runtime reflection).

**Diagnostic IDs:** MKT001, MKT002, MKT003
