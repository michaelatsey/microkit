# microkit-auth-permission-model

## Permission as Value Object

`Permission` is a sealed record — never a raw string at boundary crossings.

```csharp
// ✅ Correct
public sealed record Permission(string Resource, string Action)
{
    public static Permission Of(string resource, string action) => new(resource, action);
    public override string ToString() => $"{Resource}:{Action}";
}

// ❌ Forbidden
void Check(string permission) { ... }

// ✅ Required
ValueTask<Result<bool>> HasPermissionAsync(Permission permission, CancellationToken ct = default);
```

---

## Permission Format

```
{resource}:{action}
```

- `resource` — lowercase, kebab-case, domain noun (`audits`, `non-conformities`, `stock`)
- `action` — lowercase verb (`read`, `create`, `update`, `delete`, `validate`, `close`, `generate`)

---

## Permission Registry

All permissions are declared statically in typed registry classes.

```csharp
// ✅ Required pattern
public static class AuditPermissions
{
    public static readonly Permission Read     = Permission.Of("audits", "read");
    public static readonly Permission Create   = Permission.Of("audits", "create");
    public static readonly Permission Validate = Permission.Of("audits", "validate");
    public static readonly Permission Delete   = Permission.Of("audits", "delete");
}
```

Rules:
- One static class per domain resource
- All permissions registered in `PermissionRegistry` at startup
- No dynamic permission creation at runtime (Phase 1)
- Wildcard support: `audits:*`, `*:read`, `*:*` — evaluated by `IPermissionChecker`

---

## Role Model

```csharp
public sealed record Role(string Name);

public static class SystemRoles
{
    public static readonly Role SuperAdmin   = Role.Of("superadmin");
    public static readonly Role Admin        = Role.Of("admin");
    public static readonly Role Manager      = Role.Of("manager");
    public static readonly Role Operator     = Role.Of("operator");
    public static readonly Role Auditor      = Role.Of("auditor");
    public static readonly Role Viewer       = Role.Of("viewer");
}
```

Rules:
- Roles are typed value objects — not raw strings
- Role → Permission mapping is configurable at DI registration
- No hardcoded role checks in domain logic — use `IPermissionChecker` instead
- Roles are additive — a user with multiple roles accumulates all permissions

---

## Permission Evaluation Order

```
1. SuperAdmin check   → bypass all permission checks (explicit opt-in only)
2. Direct permission  → user has explicit permission assignment
3. Role permission    → user's role grants the permission
4. Wildcard match     → e.g. audits:* matches audits:create
5. Deny              → Result<bool>.Success(false)
```

---

## Multi-Tenant Scoping

In a multi-tenant context, permissions are **always scoped to a tenant**:

```csharp
// ✅ Correct in multi-tenant context
ITenantPermissionChecker.HasPermissionAsync(tenantId, AuditPermissions.Create, ct);

// ⚠️ Use only for system-level operations (no tenant context)
IPermissionChecker.HasPermissionAsync(AuditPermissions.Create, ct);
```

Cross-tenant permission evaluation is **explicitly forbidden** without a deliberate bypass with justification comment.
