# MicroKit.Auth.Permissions — Deferred Features

## Context
These features were explicitly deferred from Phase 1.
Read this file before implementing any Phase 2+ work on MicroKit.Auth.Permissions.

---

## Deferred

### Wildcard Permission Evaluation
- `audits:*` → all actions on audits
- `*:read` → read-only across all resources
- `*:*` → superadmin shorthand
- `IWildcardPermissionEvaluator` — dedicated evaluator, not inline in `PermissionEvaluator`

### Permission Inheritance / Hierarchy
- Parent/child permission relationships
- `IPermissionHierarchy` abstraction

### Scoped Permission Registry
- Per-tenant or per-module permission definitions
- Currently global only

### Dynamic Permission Discovery
- Runtime registration beyond compile-time `PermissionRegistry`
- Assembly scanning via `IPermissionProvider`

### Permission Groups
- Named bundles of permissions (e.g. `AuditManager` = read + create + validate)
- `PermissionGroup` VO + `IPermissionGroupRegistry`
