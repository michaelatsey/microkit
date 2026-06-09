# MicroKit.Auth — Architectural Decisions

This document records significant architectural decisions made for MicroKit.Auth.
Each ADR is immutable once merged; superseded decisions reference the ADR that replaces them.

---

## ADR-AUTH-001: IPermissionStore scope design

**Date:** 2026-06-07
**Status:** Accepted
**Decided by:** microkit-auth-architect
**Phase:** 1

### Context

`IPermissionStore` is the infrastructure contract used by permission checker implementations to resolve
which permissions a user holds. Two checker interfaces exist:

- `IPermissionChecker` — system-level, no explicit tenant context
- `ITenantPermissionChecker` — tenant-scoped, explicit `tenantId` parameter

The initial plan defined `IPermissionStore` with only a tenant-scoped overload:
```csharp
GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken ct)
```

This left `IPermissionChecker` implementations without a corresponding store contract for system-level
permission resolution. Two options were evaluated:

**Option A — Add a system-level overload to `IPermissionStore`:**
Two overloads; one tenant-scoped, one without `tenantId`.

**Option B — Document that `IPermissionChecker` implementations resolve `tenantId` from
`ICurrentUser.TenantId` internally, making `IPermissionStore` exclusively tenant-scoped.**

### Decision

**Option A was selected:** `IPermissionStore` exposes both overloads.

```csharp
// Tenant-scoped (used by ITenantPermissionChecker implementations)
ValueTask<Result<IReadOnlyList<Permission>>> GetPermissionsAsync(
    Guid userId, Guid tenantId, CancellationToken ct = default);

// System-level (used by IPermissionChecker implementations)
ValueTask<Result<IReadOnlyList<Permission>>> GetPermissionsAsync(
    Guid userId, CancellationToken ct = default);
```

### Rationale

The rule `microkit-auth-permission-model.md` states: "Cross-tenant permission evaluation is
**explicitly forbidden** without a deliberate bypass with justification comment."

Option B would require every `IPermissionChecker` implementation to silently choose a `tenantId`
(e.g. from `ICurrentUser.TenantId`), making the tenant boundary implicit rather than explicit.
This violates the spirit of the cross-tenant rule.

Option A makes the contract explicit at the interface level: system-level queries have no tenant
parameter; tenant-scoped queries always require one. There is no ambiguity, no sentinel value, and
no implicit behaviour.

### Impact

- `IPermissionStore` has two overloads — implementations must provide both.
- `MicroKit.Auth.Permissions` (Phase 1) must implement both when providing `InMemoryPermissionStore`
  or equivalent.
- No impact on `IPermissionChecker` or `ITenantPermissionChecker` — they remain unchanged.

### Constraints Applied

- `microkit-auth-permission-model.md` — cross-tenant checks always explicit
- `microkit-auth-architecture.md` — Abstractions has zero framework dependency (both overloads are pure BCL)

---

## ADR-AUTH-002: Permission primary constructor is private — Of() is the sole valid construction path

**Date:** 2026-06-07
**Status:** Accepted
**Decided by:** microkit-auth-architect
**Phase:** 1 (applied during preview, zero blast radius)

### Context

`Permission` was initially declared with a public primary constructor:

```csharp
public sealed record Permission(string Resource, string Action);
```

This allowed any consumer to write:

```csharp
new Permission("", "")         // bypasses Of() validation — no ArgumentException raised
new Permission(null!, "read")  // bypasses Of() validation — no NullReferenceException raised
```

The `Of()` factory enforces the formatting contract (non-null, non-whitespace). The public
primary constructor silently undermined it — a consumer who discovered `new Permission(...)`
through IDE completion had no signal that it was the wrong path.

The API reviewer raised this as Note 3. Two options were evaluated:

**Option A — Make the primary constructor `internal`:**  
Closes the gap for external assemblies. Internal test code and generator output could still
misuse it without going through `Of()`.

**Option B — Make the primary constructor `private`, properties `{ get; }` only:**  
Closes the gap universally. `Of()` is the only construction path. `with` expressions are
also blocked externally (no `init` setter), which is semantically correct for domain constants.

### Decision

**Option B was selected.** The primary constructor is replaced with a `private` constructor.
Properties are `{ get; }` only (not `{ get; init; }`).

```csharp
public sealed record Permission
{
    private Permission(string resource, string action)
    {
        Resource = resource;
        Action = action;
    }

    public string Resource { get; }
    public string Action { get; }

    public static Permission Of(string resource, string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return new(resource, action);
    }

    public override string ToString() => $"{Resource}:{Action}";
}
```

### Rationale

1. **Zero blast radius at preview.** No call site in the monorepo used `new Permission(...)`
   before this change. Post-stable-release the same change is a semver-MAJOR breaking change.
   Preview exists to close design gaps at zero cost.

2. **Rule enforcement is mechanical, not documentary.** A public constructor with a doc comment
   saying "prefer `Of()`" relies on discipline. A private constructor produces CS0122 at compile
   time. The stronger constraint was always correct; the primary-constructor form was an omission,
   not a deliberate design choice.

3. **Consistency with `AuthorizationResult`.** The same session applied `private AuthorizationResult()`
   to prevent default-construction bypass. `Permission` receives the same treatment for the same reason.

4. **`with` expressions are semantically incorrect for domain constants.** Permissions are compile-time
   constants declared in typed registry classes (e.g. `AuditPermissions.Create`). Transforming them
   in flight via `with { Resource = "x" }` is not a valid use case. Blocking `with` from all call
   sites (a side effect of `{ get; }` only + private constructor) removes an inappropriate operation,
   not a legitimate one.

5. **Record equality and `ToString()` are unaffected.** The C# compiler generates `Equals` and
   `GetHashCode` from all `public` properties. Both `Resource` and `Action` remain `public`, so
   structural equality is preserved. `ToString()` is explicitly overridden to `"{Resource}:{Action}"`
   and is not compiler-generated.

### Impact

- `new Permission(...)` from any call site is CS0122 (inaccessible due to protection level).
- `Of()` is the only factory. Existing registry patterns (`static readonly Permission Read = Of(...)`)
  are unaffected.
- Positional deconstruct `var (r, a) = p;` is no longer available. Consumers use `p.Resource` /
  `p.Action` directly.
- `with` expressions on `Permission` values from external assemblies are blocked. This is intentional.
- Record `==`, `.Equals()`, `GetHashCode()` work correctly — generated from `Resource` + `Action`.

### Constraints Applied

- `microkit-auth-permission-model.md` — "Always create permissions through the static factory `Of`"
  — now mechanically enforced at compile time ✅
- `microkit-auth-architecture.md` — "All contracts are interfaces or sealed records" — `sealed record`
  preserved ✅
- CLAUDE.md rule: "Permission is a VO — never pass raw permission strings across boundaries" ✅

---

## ADR-AUTH-003: ICurrentUserAccessor temporary duplication with MicroKit.MediatR.Abstractions

**Date:** 2026-06-07
**Status:** Accepted — temporary
**Decided by:** Ange-Michaël Atsé
**Phase:** 1

### Context

`ICurrentUserAccessor` was previously declared in `MicroKit.MediatR.Abstractions` as a pragmatic
v1 placement (ADR-008 in MicroKit.MediatR). ADR-008 explicitly documented the trigger condition
for promotion to a future `MicroKit.Abstractions` package:

> "A second module (e.g., MicroKit.Auth) needs to reference ICurrentUserAccessor and would
> otherwise take a dependency on MicroKit.MediatR.Abstractions purely for this interface."

During MicroKit.Auth.Abstractions implementation, `ICurrentUserAccessor` was declared independently
in `MicroKit.Auth.Abstractions`. This triggered the ADR-008 condition.

### Decision

Accept the temporary duplication. Do NOT make `MicroKit.Auth.Abstractions` depend on
`MicroKit.MediatR.Abstractions` to resolve it — that would create an incorrect dependency edge
(Auth is Level 1, MediatR is Level 2; Auth → MediatR is forbidden by the dependency graph).

The correct resolution is to create a `MicroKit.Abstractions` package (Level 0) and move
`ICurrentUserAccessor` there. Both `MicroKit.MediatR.Abstractions` and `MicroKit.Auth.Abstractions`
would then depend on it.

### Trigger for resolution

Bootstrap `MicroKit.Abstractions` when any of the following is true:
- A third module needs `ICurrentUserAccessor`
- Any other cross-cutting primitive emerges that spans 2+ modules
- MicroKit.Auth Phase 1 is complete and stable

### Consequences

- Two incompatible `ICurrentUserAccessor` interfaces exist temporarily
- Consumers integrating both MicroKit.MediatR and MicroKit.Auth must register both
- `microkit-auth-dependency-guardian` must flag any attempt to resolve this via
  Auth → MediatR dependency
- This ADR is superseded when `MicroKit.Abstractions` is bootstrapped

---

## ADR-AUTH-004: InMemoryPermissionStore registered as singleton via services.Replace()

**Date:** 2026-06-09
**Status:** Accepted
**Decided by:** microkit-auth-architect
**Phase:** 1

### Context

`AddMicroKitAuthCore()` registers `NullPermissionStore` as `IPermissionStore` via `TryAddScoped`.
The scoped lifetime is correct for production stores that read from request-scoped services (e.g. EF Core
`DbContext`). `InMemoryPermissionStore` is different: it holds all mappings in memory, resolved once at
startup, and is inherently safe as a singleton.

Three registration strategies were evaluated for `AddInMemoryPermissions()`:

**Option A — `services.TryAddSingleton<IPermissionStore, InMemoryPermissionStore>()`**  
Would leave the scoped `NullPermissionStore` descriptor in place (TryAdd is no-op when any descriptor
for the service type already exists). Result: two conflicting descriptors; the scoped NullPermissionStore
wins in scoped contexts, making InMemoryPermissionStore unreachable.

**Option B — `services.AddSingleton<IPermissionStore, InMemoryPermissionStore>()`**  
Adds a second descriptor alongside the scoped NullPermissionStore. Both are returned by
`GetServices<IPermissionStore>()`. `GetRequiredService<IPermissionStore>()` returns the last one
registered (InMemoryPermissionStore). Fragile: ordering-dependent behaviour, plus two descriptors remain.

**Option C — `services.Replace(ServiceDescriptor.Singleton<IPermissionStore>(factory))`**  
Removes the existing descriptor entirely before registering the singleton. Result: exactly one
descriptor for `IPermissionStore`, correct singleton lifetime, no leftover scoped entry.

### Decision

**Option C was selected.** `AddInMemoryPermissions()` uses `services.Replace()` to ensure a clean,
unambiguous registration with the correct lifetime.

### Rationale

`Replace()` is the authoritative way to swap a DI registration. It makes intent explicit: "I am
replacing whatever was registered before". Options A and B produce subtle, order-dependent behaviour
that is difficult to diagnose when `IPermissionStore` is resolved unexpectedly.

The `ServiceCollectionDescriptorExtensions.Replace()` method is part of
`Microsoft.Extensions.DependencyInjection.Abstractions` — no dependency on the full DI package is
required.

### Impact

- `MicroKit.Auth.Permissions.csproj` adds `Microsoft.Extensions.DependencyInjection.Abstractions`
  (already version-pinned in CPM).
- `AddInMemoryPermissions()` XML documentation explains the Replace() rationale to prevent future
  maintainers from "simplifying" it to `AddSingleton()`.
- Any future `IPermissionStore` implementation (EF Core, Redis) that also calls `Replace()` will
  correctly supersede InMemoryPermissionStore.

### Constraints Applied

- `microkit-auth-architecture.md` — Authorization packages depend on Abstractions only ✅
- `microkit-auth-dependencies.md` — Approved NuGet: `Microsoft.Extensions.DependencyInjection.Abstractions` ✅

---

## ADR-AUTH-005: PermissionRegistry deduplication and immutability contract

**Date:** 2026-06-09
**Status:** Accepted
**Decided by:** microkit-auth-architect
**Phase:** 1

### Context

`PermissionRegistry` is a compile-time catalog of all `Permission` values declared by the application.
Two design questions required explicit decisions:

1. **Deduplication strategy** — How to handle duplicate `Register()` calls for the same permission.
2. **Immutability contract** — Whether the registry snapshot is frozen after startup.

Two approaches were evaluated for deduplication:

**Option A — Parallel `HashSet<Permission>` + `List<Permission>`**  
`HashSet` provides O(1) duplicate detection using `Permission`'s auto-generated structural equality
(from `sealed record`). `List` preserves insertion order for stable enumeration. Both are updated in
`Register()`: add to `HashSet` first; if `true` (new entry), also add to `List`. Zero duplicate entries,
stable order, O(1) lookup.

**Option B — `List<Permission>` with `.Contains()` guard**  
Simpler structure but O(n) per insert. Acceptable for small catalogs (< 100 permissions) but inconsistent
with the O(1) contract implied by `Contains()`.

For immutability:

**Option X — Snapshot on first read** — Build a frozen copy lazily.  
**Option Y — Live reference** — `All` returns the live `List<Permission>` as `IReadOnlyList<Permission>`.
Population only ever occurs during DI setup (single-threaded), so the live reference is safe at runtime.

### Decision

**Option A + Option Y.** Parallel `HashSet` + `List` for deduplication; live reference for `All`.

### Rationale

1. **Option A** is the correct choice when `Contains()` is part of the public API. The `Contains()`
   contract implies O(1) lookup; a `List`-backed implementation would violate that implicit contract
   for large registries.

2. **`sealed record` structural equality is sufficient.** `Permission` auto-generates `Equals` and
   `GetHashCode` from `Resource` + `Action`. No custom equality comparer is needed.

3. **Live reference is safe.** The registry is populated once during the DI configuration phase
   (single-threaded `WebApplication.CreateBuilder` → `services.AddPermissionRegistry()`). After startup,
   `All` is treated as read-only. Documenting this contract in XML docs (REVISE 3 from architect review)
   is sufficient; adding a frozen snapshot would add complexity with no real-world benefit.

### Impact

- `PermissionRegistry` is not thread-safe for concurrent `Register()` calls. XML docs state this.
- `All` returns a live reference — reflects any subsequent `Register()` calls. This is intentional
  and documented.
- `Contains()` is O(1) — suitable for use in hot paths (e.g., policy validation at startup).

### Constraints Applied

- `microkit-auth-permission-model.md` — "All permissions registered in PermissionRegistry at startup" ✅
- `microkit-auth-naming.md` — `sealed class` for services ✅ (registry is not a VO)

---

## ADR-AUTH-006: Role-to-permission expansion in PermissionEvaluator — Option B (complete)

**Date:** 2026-06-09
**Status:** Accepted
**Decided by:** microkit-auth-architect
**Phase:** 1

### Context

The `microkit-auth-permission-model.md` rule defines a four-step permission evaluation order:

```
1. SuperAdmin check   → bypass all permission checks
2. Direct permission  → user has explicit permission assignment
3. Role permission    → user's role grants the permission
4. Wildcard match     → e.g. audits:* matches audits:create
5. Deny
```

After the initial Core (`MicroKit.Auth`) and Permissions (`MicroKit.Auth.Permissions`) packages were
merged to `dev`, `PermissionEvaluator` implemented steps 1, 2, and 4 (wildcard is part of `Matches()`),
but step 3 — role-based permission expansion — was not implemented. No contract existed for
mapping a `Role` to its granted `Permission` values.

When the `MicroKit.Auth.Roles` package was planned, two options were evaluated for resolving this gap:

**Option A — Defer (smaller scope)**
Deliver role plumbing only (`IRoleStore`, `RoleRegistry`, `SystemRoles`, `InMemoryRoleStore`).
`PermissionEvaluator` remains unchanged. Step 3 is deferred to a follow-up PR once a role→permission
bridge contract has been independently designed and reviewed.

**Option B — Complete (same PR)**
Deliver role plumbing and the role→permission bridge in the same PR:
- Add `IRolePermissionMap` to Abstractions
- Add `NullRolePermissionMap` to Core (null-object for step 3)
- Update `PermissionEvaluator` constructor: add `IRolePermissionMap roleMap`; after store check,
  iterate `ICurrentUser.Roles` and expand via `roleMap.GetPermissionsForRole(role)`
- Add `InMemoryRolePermissionMap` + `InMemoryRolePermissionMapOptions` in Roles
- `AddInMemoryRoles()` accepts an optional `configureMap` parameter for the map

### Decision

**Option B was selected.** Role-to-permission expansion is delivered in the same PR as `MicroKit.Auth.Roles`.

### Rationale

1. **Option A leaves the permission evaluation order permanently broken.** Shipping `MicroKit.Auth.Roles`
   without step 3 means roles assigned to users via `IRoleStore` or JWT claims would have no effect on
   permission checks — a misleading API. Consumers would call `HasRoleAsync` and find roles working, but
   `HasPermissionAsync` would ignore them silently.

2. **`IRolePermissionMap` is a natural Abstractions contract.** It depends only on `Permission` and `Role`,
   both already in Abstractions. Adding it introduces no new dependencies and produces no layer-boundary
   violation.

3. **Delivering both in one PR reduces the blast radius.** The `PermissionEvaluator` constructor change
   (adding `IRolePermissionMap` as a third parameter) is a breaking change at preview. Deferring it to a
   second PR would mean two separate breaking-change windows for a single logical feature.

### Key Design Choices

#### 1. `IRolePermissionMap` is synchronous

```csharp
IReadOnlyList<Permission> GetPermissionsForRole(Role role);
```

Role-to-permission mapping is static configuration resolved once during the DI setup phase — it is not
a data-access operation. Making the contract synchronous avoids unnecessary async overhead in the
`PermissionEvaluator` hot path and signals clearly to implementors that this map is not a live query.
A tenant-scoped async overload may be added in Phase 2 if per-tenant role customisation from a data
source is required.

#### 2. Tenant-agnostic in Phase 1

`IRolePermissionMap.GetPermissionsForRole(Role role)` takes no `tenantId` parameter. In Phase 1, all
roles grant the same permissions regardless of tenant. This assumption is encoded in the XML docs as an
explicit Phase 1 scope note. A future `GetPermissionsForRole(Role role, Guid tenantId, CancellationToken ct)`
overload is deferred to Phase 2.

#### 3. Role expansion uses `ICurrentUser.Roles` (JWT), not `IRoleStore`

`PermissionEvaluator` iterates `user.Roles` (populated from JWT claims by `ClaimsMapper`) for role
expansion. It does **not** call `IRoleStore`. This is an intentional scope boundary:

- `IRoleStore` / `IRoleChecker` / `ITenantRoleChecker` answer: "Does this user have this role?"
- `IRolePermissionMap` / `PermissionEvaluator` answer: "Does this role grant this permission?"

Consulting `IRoleStore` during permission evaluation would add an async store round-trip to every
`HasPermissionAsync` call, even when the store is a no-op. JWT roles are already resolved synchronously
from the token before the evaluator is invoked. Phase 1 role-based permission expansion therefore relies
exclusively on JWT roles; dynamically assigned roles from the store affect role membership checks only.

#### 4. Both `HasPermissionAsync` overloads receive role expansion

Both the system-level and tenant-scoped overloads of `PermissionEvaluator` include the same role-expansion
loop after the store check. The role→permission map is tenant-agnostic in Phase 1, so the same expansion
logic is correct in both contexts.

#### 5. `InMemoryRoleStoreOptions` and `InMemoryRolePermissionMapOptions` are separate classes

Role assignment (who has a role) and role-to-permission mapping (what a role grants) are distinct concerns
with different configuration shapes and different consumers. Merging them into a single options class would
couple the two concerns and prevent registering a custom `IRolePermissionMap` independently of `IRoleStore`.
`AddInMemoryRoles()` accepts a second optional `Action<InMemoryRolePermissionMapOptions>? configureMap`
parameter; when `null`, the existing `IRolePermissionMap` registration (i.e. `NullRolePermissionMap`) is
left unchanged, allowing consumers to supply their own map via a different extension.

### Tracked Technical Debt: `SuperAdminRoleName` raw string in Core

`PermissionEvaluator` contains:

```csharp
private const string SuperAdminRoleName = "superadmin";
```

The semantically correct reference is `SystemRoles.SuperAdmin.Name` from `MicroKit.Auth.Roles`. However,
`MicroKit.Auth` (Core) must not reference `MicroKit.Auth.Roles` — that direction would invert the
dependency graph (Core is lower than Roles in the layer hierarchy). The raw string constant is therefore
intentional and correct for Phase 1.

Resolution path: when `MicroKit.Abstractions` (Level 0 package) is bootstrapped per ADR-AUTH-003, a
`SystemRoleNames` or equivalent constants class can be placed there and referenced by both Core and Roles.

### Impact

**`MicroKit.Auth.Abstractions`**
- New `IRolePermissionMap` interface: `IReadOnlyList<Permission> GetPermissionsForRole(Role role)`
- All existing Abstractions contracts remain unchanged

**`MicroKit.Auth` (Core)**
- `PermissionEvaluator` constructor gains `IRolePermissionMap roleMap` as a third parameter.
  This is a breaking change for any code constructing `PermissionEvaluator` with `new`. At preview
  stage the blast radius is zero (no published consumers; monorepo-internal callers updated in the
  same PR). DI registration via `AddMicroKitAuthCore()` auto-resolves the new parameter transparently.
- `NullRolePermissionMap` added: `internal sealed class`, always returns `Array.Empty<Permission>()`.
  Registered as singleton in `AddMicroKitAuthCore()` via `TryAddSingleton<IRolePermissionMap, NullRolePermissionMap>()`.
- `ServiceCollectionExtensions.AddMicroKitAuthCore()` additionally registers: `IRoleStore` (scoped, NullRoleStore),
  `RoleEvaluator` (scoped), `IRoleChecker` and `ITenantRoleChecker` (both delegate to the scoped `RoleEvaluator`),
  and `IRolePermissionMap` (singleton, NullRolePermissionMap).

**`MicroKit.Auth.Roles`**
- `InMemoryRolePermissionMapOptions`: public `sealed class`, `Map(Role, params Permission[])` fluent API
- `InMemoryRolePermissionMap`: `internal sealed class` implementing `IRolePermissionMap`
- `ServiceCollectionExtensions.AddInMemoryRoles()`: optional `configureMap` parameter added;
  when provided, replaces the singleton `IRolePermissionMap` via `services.Replace()` (same pattern as ADR-AUTH-004)

### Constraints Applied

- `microkit-auth-permission-model.md` — Step 3 of the permission evaluation order is now fully implemented ✅
- `microkit-auth-architecture.md` — `IRolePermissionMap` in Abstractions, `NullRolePermissionMap` in Core, implementation in Roles — correct layer assignments ✅
- `microkit-auth-dependencies.md` — No forbidden dependency edges introduced ✅
- ADR-AUTH-004 — `services.Replace()` pattern reused for `IRolePermissionMap` registration ✅
- ADR-AUTH-005 — `InMemoryRolePermissionMapOptions` accumulates permissions with deduplication via `HashSet<Permission>` ✅
