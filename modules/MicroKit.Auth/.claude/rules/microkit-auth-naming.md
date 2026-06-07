# microkit-auth-naming

## General Rules

- `sealed record` — Value Objects, events, options, results
- `sealed class` — services, handlers, middleware, validators, mappers
- `interface` prefix `I` — all contracts in Abstractions
- No `Base` suffix — use composition, not inheritance
- No `Helper`, `Utils`, `Manager` suffix — name by responsibility

---

## Interfaces

| Pattern | Example |
|---------|---------|
| `I{Noun}{Verb}er` | `IPermissionChecker`, `IJwtValidator`, `IClaimsMapper` |
| `I{Noun}Accessor` | `ICurrentUserAccessor` |
| `I{Noun}Store` | `IPermissionStore` |
| `I{Noun}Provider` | `IAuthenticationProvider` |

---

## Implementations

| Pattern | Example |
|---------|---------|
| `{Noun}{Verb}er` | `PermissionChecker`, `JwtValidator`, `ClaimsMapper` |
| `{Provider}{Noun}` | `SupabaseClaimsMapper`, `SupabaseJwtValidator` |
| `{Noun}Accessor` | `CurrentUserAccessor` |
| `Fake{Noun}` | `FakeCurrentUser`, `FakePermissionChecker` (Testing only) |

---

## Value Objects & Records

| Pattern | Example |
|---------|---------|
| `{Noun}` | `Permission`, `Role`, `UserId`, `TenantId` |
| `{Noun}Options` | `JwtValidationOptions`, `SupabaseAuthOptions` |
| `{Noun}Result` | `AuthorizationResult` |

---

## Static Registry Classes

| Pattern | Example |
|---------|---------|
| `{Resource}Permissions` | `AuditPermissions`, `NonConformityPermissions` |
| `SystemRoles` | (singleton — all built-in roles) |

---

## Middleware

| Pattern | Example |
|---------|---------|
| `{Concern}Middleware` | `CurrentUserMiddleware`, `PermissionEnrichmentMiddleware` |

---

## Authorization Attributes

| Pattern | Example |
|---------|---------|
| `Require{Concern}Attribute` | `RequirePermissionAttribute`, `RequireRoleAttribute` |

---

## DI Extension Methods

| Pattern | Example |
|---------|---------|
| `Add{Module}Auth()` | `AddMicroKitAuth()` on `IServiceCollection` |
| `Use{Module}Auth()` | `UseMicroKitAuth()` on `IApplicationBuilder` |
| `Add{Provider}()` | `AddSupabase()`, `AddKeycloak()` on `MicroKitAuthBuilder` |

---

## Test Methods

```
{Method}_{Scenario}_{ExpectedResult}

Examples:
  HasPermissionAsync_WhenUserHasPermission_ReturnsTrue
  ValidateAsync_WhenTokenExpired_ReturnsFailure
  MapFromClaims_WhenSubMissing_ReturnsFailure
  HasPermissionAsync_WhenRoleGrantsPermission_ReturnsTrue
```

---

## Files

| Type | Convention | Example |
|------|-----------|---------|
| Interface | `I{Name}.cs` | `ICurrentUser.cs` |
| Implementation | `{Name}.cs` | `CurrentUser.cs` |
| Options | `{Name}Options.cs` | `JwtValidationOptions.cs` |
| Extensions | `{Name}Extensions.cs` | `ServiceCollectionExtensions.cs` |
| Middleware | `{Name}Middleware.cs` | `CurrentUserMiddleware.cs` |
| Tests | `{Name}Tests.cs` | `PermissionCheckerTests.cs` |

---

## Prefixes to avoid

```
❌ AuthHelper, AuthUtils, AuthManager
❌ BasePermissionChecker, AbstractJwtValidator
❌ PermissionCheckerImpl, JwtValidatorDefault
```
