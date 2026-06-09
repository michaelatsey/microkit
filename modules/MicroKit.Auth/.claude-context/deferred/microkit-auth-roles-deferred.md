# MicroKit.Auth.Roles — Deferred Features

## Context
These features were explicitly deferred from Phase 1.
Read this file before implementing any Phase 2+ work on MicroKit.Auth.Roles.

---

## Deferred

### SuperAdminRoleName constant
- `PermissionEvaluator` uses raw string `"superadmin"` (intentional — Core cannot depend on Roles)
- Resolution: move constant to `MicroKit.Abstractions` Level 0 (ADR-AUTH-003 trigger)

### Role Inheritance
- Parent/child role hierarchy
- `IRoleHierarchy` abstraction

### Tenant-scoped Role Registry
- Per-tenant role definitions (currently global only)
