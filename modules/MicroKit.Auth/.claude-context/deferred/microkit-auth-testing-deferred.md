# MicroKit.Auth.Testing — Deferred Features

## Context
These features were explicitly deferred from Phase 1.
Read this file before implementing any Phase 2+ work on MicroKit.Auth.Testing.

---

## Deferred

### AuthTestFixture
- Full DI setup helper for integration tests consuming MicroKit.Auth
- `AuthTestFixture` — pre-wired `IServiceCollection` with all Phase 1 Auth services registered
- Configurable via fluent API: `WithPermissions(...)`, `WithRoles(...)`, `WithCurrentUser(...)`
- **Why deferred:** no real consumer exists yet (SaaS BTP not started) — fixture shape must
  emerge from actual usage patterns, not speculation
- Phase 2: implement once SaaS BTP integration tests reveal the real assembly needs
