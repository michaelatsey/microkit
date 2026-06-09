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
