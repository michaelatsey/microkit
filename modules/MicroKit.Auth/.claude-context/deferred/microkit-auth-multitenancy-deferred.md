# MicroKit.Auth.Multitenancy — Deferred Features

## Context
These features were explicitly deferred from Phase 1.
Read this file before implementing any Phase 2+ work on MicroKit.Auth.Multitenancy.

---

## Deferred

### Per-Tenant Permission Override
- Tenant-specific permission grants that override global registry
- `ITenantPermissionOverrideStore` — per-tenant permission customization

### Per-Tenant Role Override
- Tenant-specific role definitions beyond global `SystemRoles`
- `ITenantRoleStore` extension for custom tenant roles

### Tenant Impersonation
- Admin impersonating a user within a tenant context
- `ITenantImpersonationContext` — audit trail mandatory
- Requires `MicroKit.Auth.Audit` (Phase 2)

### Cross-Tenant Authorization
- Explicit cross-tenant permission evaluation with justification comment (ADR rule)
- `ICrossTenantPermissionChecker` — deliberate bypass pattern

### Tenant Isolation Policies
- Policy-level enforcement of tenant boundaries
- Bridge with `MicroKit.Auth.Policies` (Phase 2)

### Tenant-Scoped Token Claims
- Inject `tenantId` into JWT claims at generation time
- Requires `MicroKit.Auth.Jwt` + `IJwtClaimsEnricher` (deferred in Jwt)

### CurrentUserTenantSynchronizer — Non-HTTP Tenant Context Push

**Deferred by:** ADR-AUTH-008 (2026-06-10)

**What:** A `CurrentUserTenantSynchronizer` service that reads the current user via
`ICurrentUserAccessor` and explicitly pushes the resolved `ITenantInfo` into `ITenantContextAccessor`.

**Why deferred:** In the HTTP path, `TenantResolutionMiddleware` (from `MicroKit.Multitenancy.AspNetCore`)
already handles this via the standard pipeline once `AuthTenantResolutionStrategy` is registered.
The non-HTTP niche (background jobs, queues, gRPC workers) requires a `CreateScope`-based design per
`multitenancy-async-context.md` — the correct pattern is to capture the tenant context before spawning
background work and restore it via `ITenantContextAccessor.CreateScope(tenant)`, not to push ad-hoc.

**Phase 2 design hints:**
- Constructor: `(ICurrentUserAccessor userAccessor, ITenantContextAccessor tenantAccessor, ITenantStore tenantStore)`
- Method: `ValueTask SynchronizeAsync(CancellationToken ct = default)`
  - If user has TenantId → `tenantStore.FindAsync(new TenantId(user.TenantId.Value), ct)` → `tenantAccessor.SetTenant(info)`
  - If user has no TenantId or is unauthenticated → `tenantAccessor.SetTenant(null)`
- Lifetime: `Scoped` — depends on Scoped `ICurrentUserAccessor` and `ITenantContextAccessor`
- Provide a companion middleware or `IHostedService` integration point for non-HTTP hosts
- See `multitenancy-async-context.md` §"Background work — must use CreateScope" before implementing
