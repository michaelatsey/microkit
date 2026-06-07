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
