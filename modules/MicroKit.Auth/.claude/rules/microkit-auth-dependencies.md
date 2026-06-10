# microkit-auth-dependencies

## Dependency Graph (authoritative)

```
MicroKit.Auth.Abstractions
    ← MicroKit.Result
    ← Microsoft.Extensions.DependencyInjection.Abstractions

MicroKit.Auth (Core)
    ← MicroKit.Auth.Abstractions

MicroKit.Auth.AspNetCore
    ← MicroKit.Auth
    ← Microsoft.AspNetCore.App (FrameworkReference)

MicroKit.Auth.Permissions
    ← MicroKit.Auth.Abstractions

MicroKit.Auth.Roles
    ← MicroKit.Auth.Abstractions
    ← MicroKit.Auth.Permissions

MicroKit.Auth.Policies                            [Phase 2]
    ← MicroKit.Auth.Abstractions
    ← MicroKit.Auth.Permissions
    ← MicroKit.Auth.Roles

MicroKit.Auth.Jwt
    ← MicroKit.Auth.Abstractions
    ← Microsoft.IdentityModel.Tokens
    ← System.IdentityModel.Tokens.Jwt

MicroKit.Auth.Supabase
    ← MicroKit.Auth (Core)         # was MicroKit.Auth.Jwt — updated per ADR-AUTH-007
    ← MicroKit.Auth.AspNetCore
    ← Microsoft.IdentityModel.JsonWebTokens (JWKS / ES256)
    ← Microsoft.IdentityModel.Tokens
    ← Microsoft.Extensions.Http

MicroKit.Auth.OpenIdConnect                       [Phase 2]
    ← MicroKit.Auth.Jwt
    ← Microsoft.AspNetCore.Authentication.OpenIdConnect

MicroKit.Auth.ApiKeys                             [Phase 2]
    ← MicroKit.Auth.Abstractions

MicroKit.Auth.ServiceAccounts                     [Phase 2]
    ← MicroKit.Auth.Abstractions
    ← MicroKit.Auth.Jwt

MicroKit.Auth.Multitenancy
    ← MicroKit.Auth.Abstractions
    ← MicroKit.Multitenancy.Abstractions

MicroKit.Auth.MediatR                                [Phase 2]
    ← MicroKit.Auth.Abstractions
    ← MicroKit.MediatR.Abstractions

MicroKit.Auth.Logging                             [Phase 2]
    ← MicroKit.Auth.Abstractions
    ← MicroKit.Logging.Abstractions

MicroKit.Auth.Audit                               [Phase 2]
    ← MicroKit.Auth.Abstractions
    ← MicroKit.Logging.Abstractions

MicroKit.Auth.Testing
    ← MicroKit.Auth.Abstractions
    ← MicroKit.Auth.Permissions

MicroKit.Auth.Cognito / Keycloak / Auth0 / EntraId / IdentityServer   [Phase 3]
    ← MicroKit.Auth.OpenIdConnect
```

---

## Cross-Module References — Mandatory Pattern

Any cross-module dependency MUST use the two-ItemGroup CIReleaseBuild pattern:

```xml
<!-- Local dev: source ProjectReferences -->
<!-- ⚠ Any new cross-module dependency must be added to BOTH ItemGroups -->
<ItemGroup Condition="'$(CIReleaseBuild)' != 'true'">
  <ProjectReference Include="../../../../modules/MicroKit.Result/src/MicroKit.Result/MicroKit.Result.csproj" />
</ItemGroup>
<!-- CI/Release: published NuGet packages -->
<ItemGroup Condition="'$(CIReleaseBuild)' == 'true'">
  <PackageReference Include="MicroKit.Result" />
</ItemGroup>
```

See monorepo root `.claude/rules/cross-module-references.md` for the full canonical pattern.

---

## Forbidden Dependencies

```
❌ MicroKit.Auth.Abstractions → ASP.NET Core / EF Core / any framework
❌ MicroKit.Auth.Abstractions → MicroKit.Auth (Core) — inverse only
❌ Federation providers → other Federation providers
❌ MicroKit.Auth.Testing → MicroKit.Auth (Core) or any framework
❌ Any package → MicroKit.Identity (future — never reference early)
❌ Circular dependency between any two packages
```

---

## NuGet Packages — Approved List

| Package | Allowed in |
|---------|-----------|
| `Microsoft.IdentityModel.Tokens` | Jwt, Supabase |
| `Microsoft.IdentityModel.JsonWebTokens` | Supabase (JWKS / ES256) |
| `System.IdentityModel.Tokens.Jwt` | Jwt only |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | AspNetCore, Supabase |
| `Microsoft.AspNetCore.Authentication.OpenIdConnect` | OpenIdConnect only |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | Abstractions, Core |
| `Microsoft.Extensions.Options` | Core, AspNetCore |
| `Microsoft.Extensions.Http` | Supabase (JWKS fetch) |

> Any package not on this list requires `microkit-auth-dependency-guardian` approval before adding.

---

## Directory.Packages.props

All version pins live in the monorepo root `build/Directory.Packages.props`.
Never add `Version=` attribute directly in `.csproj` files.
