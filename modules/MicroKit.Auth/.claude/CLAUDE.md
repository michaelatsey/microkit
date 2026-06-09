# MicroKit.Auth — Module Brain

## 🎯 Purpose

MicroKit.Auth is **not an identity management library**. It is the **authorization and security context layer** of the MicroKit ecosystem.

Its mission: consume identity tokens from any OIDC/JWT provider, build a strongly-typed security context (`ICurrentUser`), and enforce fine-grained permission checks — without coupling consumers to a specific identity system.

> **Core principle:** authentication is delegated (Supabase, Keycloak, Auth0, Entra ID…). MicroKit.Auth owns authorization only.

```
Identity Provider (Supabase / Keycloak / Auth0 / Entra ID)
        │
        ▼ JWT / OIDC
MicroKit.Auth                ← token validation, security context, RBAC
        │
        ├── MediatR Handlers    ← ICurrentUser, IPermissionChecker
        ├── Domain Services  ← permission-aware business logic
        └── APIs / Workers   ← policy enforcement, claims enrichment
```

---

## 🗺️ Navigation

Always load the relevant file before working on a specific concern:

| Task | Load first | Agent |
|------|-----------|-------|
| **Implementing anything new** | `.claude/CLAUDE.md` + relevant rule | `microkit-auth-implementer` — plan before code |
| Architecture / contract decision | `.claude/rules/microkit-auth-architecture.md` + `.claude-context/context/microkit-auth-architectural-decisions.md` | `microkit-auth-architect` |
| Permission model concern | `.claude/rules/microkit-auth-permission-model.md` + `.claude-context/standards/microkit-auth-permission-contracts.md` | `microkit-auth-architect` |
| JWT validation concern | `.claude/rules/microkit-auth-jwt.md` + `.claude-context/standards/microkit-auth-jwt-validation.md` | `microkit-auth-implementer` |
| Supabase provider concern | `.claude/rules/microkit-auth-supabase.md` | `microkit-auth-implementer` |
| Multi-tenancy bridge | `.claude/rules/microkit-auth-multitenancy.md` | `microkit-auth-architect` |
| Public API change | `.claude/rules/microkit-auth-abstractions.md` + `.claude/rules/microkit-auth-naming.md` | `microkit-auth-api-reviewer` — required before merge |
| Dependency / `.csproj` change | `.claude/rules/microkit-auth-dependencies.md` + `.claude-context/context/microkit-auth-dependency-graph.md` | `microkit-auth-dependency-guardian` |
| Release | `.claude/workflows/microkit-auth-releasing.md` + `/microkit-auth-release` | `microkit-auth-release-manager` |

---

## 🏛️ Module Structure

```
MicroKit.Auth/
├── src/
│   │
│   │  ── Core ──────────────────────────────────────────────────────────
│   ├── MicroKit.Auth.Abstractions/        ← pure contracts, zero framework dep
│   ├── MicroKit.Auth/                     ← security context, RBAC engine, claims mapping
│   ├── MicroKit.Auth.AspNetCore/          ← middleware, attributes, DI extensions
│   │
│   │  ── Authorization ──────────────────────────────────────────────────
│   ├── MicroKit.Auth.Permissions/         ← permission definitions, registry, wildcards  [Phase 1]
│   ├── MicroKit.Auth.Roles/               ← role definitions, inheritance, mapping       [Phase 1]
│   ├── MicroKit.Auth.Policies/            ← typed policies, policy builder               [Phase 2]
│   │
│   │  ── Authentication ─────────────────────────────────────────────────
│   ├── MicroKit.Auth.Jwt/                 ← JWT validation, JWKS, key rotation           [Phase 1]
│   ├── MicroKit.Auth.ApiKeys/             ← API key validation, expiry, revocation        [Phase 2]
│   ├── MicroKit.Auth.ServiceAccounts/     ← service-to-service identity, machine tokens  [Phase 2]
│   │
│   │  ── Federation ──────────────────────────────────────────────────────
│   ├── MicroKit.Auth.OpenIdConnect/       ← OIDC base: discovery, JWKS, claims mapping   [Phase 2]
│   ├── MicroKit.Auth.Supabase/            ← Supabase JWT + OIDC adapter                  [Phase 1]
│   ├── MicroKit.Auth.Cognito/             ← AWS Cognito adapter                          [Phase 3]
│   ├── MicroKit.Auth.Keycloak/            ← Keycloak realm roles adapter                 [Phase 3]
│   ├── MicroKit.Auth.Auth0/               ← Auth0 organizations adapter                  [Phase 3]
│   ├── MicroKit.Auth.EntraId/             ← Azure Entra ID groups adapter                [Phase 3]
│   └── MicroKit.Auth.IdentityServer/      ← Duende / OpenIddict adapter                  [Phase 3]
│   │
│   │  ── Integration ─────────────────────────────────────────────────────
│   ├── MicroKit.Auth.MediatR/             ← AuthorizationBehavior<TRequest,TResponse>    [Phase 2]
│   ├── MicroKit.Auth.Multitenancy/        ← bridge Auth ↔ MicroKit.Multitenancy          [Phase 1]
│   ├── MicroKit.Auth.Logging/             ← UserId/TenantId enrichment in log context    [Phase 2]
│   └── MicroKit.Auth.Audit/               ← who/when/permission audit events             [Phase 2]
│
├── tests/
│   ├── MicroKit.Auth.UnitTests/
│   ├── MicroKit.Auth.IntegrationTests/
│   ├── MicroKit.Auth.ArchitectureTests/
│   └── MicroKit.Auth.PerformanceTests/
│
├── testing/
│   └── MicroKit.Auth.Testing/             ← FakeCurrentUser, FakePermissionChecker       [Phase 1]
│
├── benchmarks/
├── docs/
└── samples/
```

---

## 📦 Dependency Graph

```
MicroKit.Auth.Abstractions
    ← MicroKit.Result
    ← zero ASP.NET / EF Core dependency

MicroKit.Auth (Core)
    ← MicroKit.Auth.Abstractions
    ← Microsoft.Extensions.DependencyInjection.Abstractions

MicroKit.Auth.AspNetCore
    ← MicroKit.Auth
    ← Microsoft.AspNetCore.App (FrameworkReference)

MicroKit.Auth.Permissions
    ← MicroKit.Auth.Abstractions

MicroKit.Auth.Roles
    ← MicroKit.Auth.Abstractions
    ← MicroKit.Auth.Permissions

MicroKit.Auth.Policies
    ← MicroKit.Auth.Abstractions
    ← MicroKit.Auth.Permissions
    ← MicroKit.Auth.Roles

MicroKit.Auth.Jwt
    ← MicroKit.Auth.Abstractions
    ← Microsoft.IdentityModel.Tokens
    ← System.IdentityModel.Tokens.Jwt

MicroKit.Auth.Supabase
    ← MicroKit.Auth.Jwt
    ← MicroKit.Auth.AspNetCore

MicroKit.Auth.OpenIdConnect
    ← MicroKit.Auth.Jwt
    ← Microsoft.AspNetCore.Authentication.OpenIdConnect

MicroKit.Auth.Multitenancy
    ← MicroKit.Auth.Abstractions
    ← MicroKit.Multitenancy.Abstractions

MicroKit.Auth.MediatR
    ← MicroKit.Auth.Abstractions
    ← MicroKit.MediatR.Abstractions

MicroKit.Auth.Testing
    ← MicroKit.Auth.Abstractions
    ← MicroKit.Auth.Permissions
```

**MicroKit.Auth is a Level 1 module** (depends on Result only at Abstractions level).

---

## 🔑 Key Contracts (quick reference)

### Security Context
```csharp
ICurrentUser                    // UserId, TenantId, Email, Roles, IsAuthenticated
ISecurityContext                // CurrentUser + request-scoped security state
ICurrentUserAccessor            // Get/Set — backed by AsyncLocal (host-agnostic)
```

### Authorization
```csharp
IPermissionChecker              // HasPermissionAsync(Permission, ct) → Result<bool>
ITenantPermissionChecker        // HasPermissionAsync(TenantId, Permission, ct) → Result<bool>
IPermissionStore                // GetPermissionsAsync(UserId, TenantId, ct) → Result<IReadOnlyList<Permission>>
IRoleChecker                    // HasRoleAsync(Role, ct) → Result<bool>
```

### Permission Model
```csharp
Permission                      // sealed record — Resource + Action → "audits:create"
Role                            // sealed record — Name (typed, not raw string)
PermissionRegistry              // static registry — compile-time permission definitions
```

### Token Validation
```csharp
IJwtValidator                   // ValidateAsync(token, ct) → Result<ClaimsPrincipal>
IJwtClaimsReader                // ReadClaims(principal) → IReadOnlyDictionary<string, string>
IClaimsMapper                   // MapToClaims(ICurrentUser) / MapFromClaims(ClaimsPrincipal)
```

### Testing
```csharp
FakeCurrentUser                 // test double — ICurrentUser
FakeCurrentUserBuilder          // fluent builder for test scenarios
FakePermissionChecker           // configurable permission responses
```

---

## 📐 Non-Negotiable Rules

1. **`ICurrentUserAccessor` NEVER injected in a singleton** — always scoped/transient
2. **`Permission` is a Value Object** — never pass raw permission strings across boundaries
3. **JWT validation never throws** — always returns `Result<ClaimsPrincipal>.Failure` on invalid token
4. **No identity management** — users, passwords, registration, MFA belong to `MicroKit.Identity` (future)
5. **Cross-tenant permission checks always explicit** — `ITenantPermissionChecker` over `IPermissionChecker` in multi-tenant context
6. **`sealed record` for VO/events** | **`sealed class` for services/handlers/strategies**
7. **`ValueTask<T>`** for all async methods | **`ConfigureAwait(false)`** throughout
8. **`CancellationToken ct = default`** always last parameter
9. **Shouldly + NSubstitute** for tests — **FluentAssertions is banned**
10. **No inline `Version=`** on `PackageReference` — CPM via `Directory.Packages.props`
11. **XML docs on all public members** in `src/` projects
12. **`[RequirePermission]` attribute uses typed `Permission`** — never raw strings

---

## 🏷️ Canonical Permission Format

```
{resource}:{action}

Examples:
  audits:read
  audits:create
  audits:validate
  nonconformities:create
  nonconformities:close
  reports:generate
  stock:update
  users:invite
  tenants:manage
```

Permission wildcards:
```
audits:*        → all actions on audits
*:read          → read-only across all resources
*:*             → superadmin (use with extreme caution)
```

> **Full reference:** `.claude-context/standards/microkit-auth-permission-contracts.md`

---

## 🤖 Available Agents

| Agent | Model | Trigger |
|-------|-------|---------|
| `microkit-auth-implementer` | Opus | **First agent to invoke** before writing any new code — produces a plan and waits for approval |
| `microkit-auth-architect` | Opus | Contract decisions, module boundary changes, permission model design |
| `microkit-auth-api-reviewer` | Opus | Public API surface in Abstractions or Core — required before merge |
| `microkit-auth-dependency-guardian` | Haiku | Any `.csproj` / project-reference change — fast PASS/BLOCK |
| `microkit-auth-release-manager` | Sonnet | `/microkit-auth-release` — full release lifecycle |

---

## ⚡ Available Commands

| Command | Purpose |
|---------|---------|
| `/microkit-auth-plan` | Run implementer agent — plan before any code |
| `/microkit-auth-new-provider` | Scaffold a new Federation provider adapter |
| `/microkit-auth-new-permission` | Add a new typed permission to the registry |
| `/microkit-auth-review-architecture` | Run architect agent against the module |
| `/microkit-auth-release` | Prepare and validate release |

---

## 🔗 Context Layer

```
.claude-context/
├── standards/
│   ├── microkit-auth-permission-contracts.md   ← canonical permission format, registry rules
│   ├── microkit-auth-jwt-validation.md          ← JWT validation constraints, JWKS rules
│   └── microkit-auth-claims-mapping.md          ← provider → ICurrentUser claim mapping
├── templates/
│   ├── microkit-auth-provider-template/         ← scaffold for new Federation provider
│   └── microkit-auth-permission-template/       ← scaffold for new permission set
└── context/
    ├── microkit-auth-architectural-decisions.md  ← ADRs
    └── microkit-auth-dependency-graph.md         ← full dep graph with rationale
```

---

## 🔢 Versioning

```json
{
  "version": "1.0",
  "publicReleaseRefSpec": [
    "^refs/heads/main$",
    "^refs/tags/auth-v\\d+\\.\\d+"
  ]
}
```

Git tag convention: `auth-v1.0.0`, `auth-v1.1.0-beta.1`
All Phase 1 packages share one version per release.

---

## 🚀 Phase Status

| Package | Phase | Status |
|---------|-------|--------|
| `MicroKit.Auth.Abstractions` | 1 | ✅ merged dev |
| `MicroKit.Auth` | 1 | ✅ merged dev |
| `MicroKit.Auth.AspNetCore` | 1 | ✅ merged dev |
| `MicroKit.Auth.Permissions` | 1 | ✅ merged dev |
| `MicroKit.Auth.Roles` | 1 | 🚧 In progress |
| `MicroKit.Auth.Jwt` | 1 | 📋 Planned |
| `MicroKit.Auth.Supabase` | 1 | 📋 Planned |
| `MicroKit.Auth.Multitenancy` | 1 | 📋 Planned |
| `MicroKit.Auth.Testing` | 1 | 📋 Planned |
| `MicroKit.Auth.Policies` | 2 | ⏳ Scaffold only |
| `MicroKit.Auth.ApiKeys` | 2 | ⏳ Scaffold only |
| `MicroKit.Auth.MediatR` | 2 | ⏳ Scaffold only |
| `MicroKit.Auth.Logging` | 2 | ⏳ Scaffold only |
| `MicroKit.Auth.Audit` | 2 | ⏳ Scaffold only |
| `MicroKit.Auth.ServiceAccounts` | 2 | ⏳ Scaffold only |
| `MicroKit.Auth.OpenIdConnect` | 2 | ⏳ Scaffold only |
| `MicroKit.Auth.Cognito` | 3 | ⏳ Scaffold only |
| `MicroKit.Auth.Keycloak` | 3 | ⏳ Scaffold only |
| `MicroKit.Auth.Auth0` | 3 | ⏳ Scaffold only |
| `MicroKit.Auth.EntraId` | 3 | ⏳ Scaffold only |
| `MicroKit.Auth.IdentityServer` | 3 | ⏳ Scaffold only |
