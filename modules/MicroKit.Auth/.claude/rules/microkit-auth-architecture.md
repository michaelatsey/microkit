# microkit-auth-architecture

## Layer Boundaries

### Abstractions
- Zero dependency on ASP.NET Core, EF Core, or any framework
- Only allowed deps: `MicroKit.Result`, `System.*`, `Microsoft.Extensions.DependencyInjection.Abstractions`
- All contracts are interfaces or sealed records
- No implementation logic — contracts only

### Core (MicroKit.Auth)
- Depends on `MicroKit.Auth.Abstractions` only (no framework)
- Contains: `CurrentUser`, `SecurityContext`, `ClaimsMapper`, `PermissionEvaluator`
- `ICurrentUserAccessor` backed by `AsyncLocal<ICurrentUser?>` — never `IHttpContextAccessor`
- DI registration via `AddMicroKitAuthCore()` extension

### AspNetCore
- Depends on `MicroKit.Auth` + `Microsoft.AspNetCore.App`
- Contains: middleware, `[RequirePermission]` attribute, `PermissionAuthorizationHandler`
- Claims transformation via `IClaimsTransformation` — runs post-authentication, pre-authorization
- DI registration via `AddMicroKitAuth()` + `UseMicroKitAuth()` extensions

### Authorization packages (Permissions, Roles, Policies)
- Depend on `MicroKit.Auth.Abstractions` only
- No ASP.NET or EF Core dependency at this layer
- `Policies` may depend on `Permissions` + `Roles`

### Authentication packages (Jwt, ApiKeys, ServiceAccounts)
- `Jwt` depends on `MicroKit.Auth.Abstractions` + `Microsoft.IdentityModel.*`
- Never depend on `MicroKit.Auth` (Core) directly — only on Abstractions

### Federation packages (Supabase, Keycloak, etc.)
- Each provider depends on its base layer (`Jwt` or `OpenIdConnect`) + `AspNetCore`
- Provider packages NEVER depend on each other
- Each provider maps its specific claims format to `ICurrentUser` via `IClaimsMapper`

### Integration packages (MediatR, Multitenancy, Logging, Audit)
- `Multitenancy` depends on `MicroKit.Auth.Abstractions` + `MicroKit.Multitenancy.Abstractions` only
- `MediatR` depends on `MicroKit.Auth.Abstractions` + `MicroKit.MediatR.Abstractions` only
- Never introduce circular dependencies

### Testing
- `MicroKit.Auth.Testing` depends on `MicroKit.Auth.Abstractions` + `MicroKit.Auth.Permissions`
- Never depends on `MicroKit.Auth` (Core) or any framework package
- Test doubles implement the Abstractions interfaces directly

---

## Forbidden Patterns

```
❌ ICurrentUserAccessor injected in singleton
❌ IHttpContextAccessor used in Core or Abstractions
❌ Raw permission strings passed across layer boundaries
❌ Provider packages depending on each other
❌ Identity management (users, passwords, sessions) anywhere in this module
❌ JWT generation in Core, AspNetCore, or any provider/integration package
   (Exception: MicroKit.Auth.Jwt may generate HMAC tokens from ICurrentUser — see ADR-AUTH-007)
❌ Console.WriteLine — use ILogger<T>
❌ Circular dependency between any two packages
```

---

## What Belongs Where

| Concern | Package |
|---------|---------|
| `ICurrentUser` contract | Abstractions |
| `CurrentUser` implementation | Core |
| JWT validation logic | Jwt |
| Supabase claims mapping | Supabase |
| `[RequirePermission]` attribute | AspNetCore |
| `Permission` value object | Abstractions |
| Permission registry | Permissions |
| Role inheritance | Roles |
| Tenant-scoped permission check | Multitenancy |
| MediatR pipeline behavior | MediatR |
| `FakeCurrentUser` test double | Testing |
