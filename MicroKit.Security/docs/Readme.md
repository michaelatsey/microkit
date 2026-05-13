# MicroKit.Security

A production-ready, extensible security ecosystem for .NET 10 — authentication, authorization, and multi-tenancy for SaaS and microservices.

---

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Installation](#installation)
4. [Quick Start](#quick-start)
5. [Package Reference](#package-reference)
   - [MicroKit.Security.Abstractions](#microkitsecurityabstractions)
   - [MicroKit.Security.Core](#microkitsecuritycore)
   - [MicroKit.Security.AspNetCore](#microkitsecurityaspnetcore)
   - [MicroKit.Security.Jwt](#microkitsecurityjwt)
   - [MicroKit.Security.ApiKey](#microkitsecurityapikey)
   - [MicroKit.Security.AzureAd](#microkitsecurityazuread)
   - [MicroKit.Security.Cognito](#microkitsecuritycognito)
   - [MicroKit.Security.MultiTenancy](#microkitsecuritymultitenancy)
6. [Configuration Reference](#configuration-reference)
7. [Advanced Scenarios](#advanced-scenarios)
8. [Best Practices](#best-practices)
9. [Troubleshooting](#troubleshooting)

---

## Overview

MicroKit.Security provides a unified, provider-agnostic authentication and authorization framework built on the principle that **authentication scheme details are implementation concerns** — your application code works against stable abstractions regardless of whether it's validating a JWT from Azure AD, an API key from Redis, or a token from AWS Cognito.

Key features:

- **Provider pattern**: JWT, API Key, Azure AD, AWS Cognito — all swappable behind `IAuthenticationProvider`
- **Two-level caching**: optional L1 (memory) + L2 (distributed) caching per provider via `CachedAuthenticationProvider`
- **Multi-tenancy aware**: built-in tenant consistency validation and `ITenantResolutionStrategy` bridge
- **Zero-allocation critical paths**: `Span<T>`, `ValueTask`, `readonly record struct` throughout
- **Abstraction-first**: `MicroKit.Security.Abstractions` has zero third-party dependencies

### Design principles

| Principle | Implementation |
|-----------|----------------|
| Immutability | `SecurityPrincipal` and `SecurityClaim` are records |
| Testability | `TimeProvider` injection, interface-based design, `NullLogger` friendly |
| Separation | `Abstractions → Core → Providers → AspNetCore integration` |
| Extensibility | Implement `IAuthenticationProvider` to plug in any scheme |

---

## Architecture

```
┌────────────────────────────────────────────────────────┐
│              MicroKit.Security.AspNetCore               │
│  SecurityMiddleware  ·  IAuthenticationExtractor        │
│  RequirePermission  ·  EndpointExtensions               │
├────────────────┬───────────────┬───────────────┬────────┤
│  Security.Jwt  │  Security     │  Security     │Security│
│  Security.Jwt  │  .ApiKey      │  .AzureAd     │.Cognito│
│  .AspNetCore   │  .ApiKey      │               │        │
│                │  .AspNetCore  │               │        │
│                │  .ApiKey      │               │        │
│                │  .RedisStore  │               │        │
├────────────────┴───────────────┴───────────────┴────────┤
│                  MicroKit.Security.Core                  │
│  AuthenticationService  ·  AuthorizationService         │
│  SecurityContextFactory  ·  CachedAuthenticationProvider│
│  ClientContextAccessor  ·  SecureHasher                 │
├─────────────────────────────────────────────────────────┤
│              MicroKit.Security.Abstractions              │
│  IAuthenticationProvider  ·  IAuthorizationService      │
│  IClientContext  ·  IClientContextAccessor               │
│  SecurityPrincipal  ·  SecurityClaim  ·  SecurityOptions │
│  AuthenticationScheme  ·  ClaimTypes  ·  ICacheableOptions│
└─────────────────────────────────────────────────────────┘
                           ↓
              MicroKit.Security.MultiTenancy
              SecurityPrincipalTenantResolutionStrategy
              (bridges IClientContextAccessor → ITenantResolutionStrategy)
```

---

## Installation

Install only the packages you need:

```bash
# Always required
dotnet add package MicroKit.Security.Abstractions
dotnet add package MicroKit.Security.Core

# ASP.NET Core middleware + extractors
dotnet add package MicroKit.Security.AspNetCore

# Authentication providers (choose as needed)
dotnet add package MicroKit.Security.Jwt
dotnet add package MicroKit.Security.Jwt.AspNetCore      # Bearer token extractor
dotnet add package MicroKit.Security.ApiKey
dotnet add package MicroKit.Security.ApiKey.AspNetCore   # Header/query extractor
dotnet add package MicroKit.Security.ApiKey.RedisStore   # Redis-backed key store
dotnet add package MicroKit.Security.AzureAd
dotnet add package MicroKit.Security.Cognito

# Multi-tenancy bridge
dotnet add package MicroKit.Security.MultiTenancy
```

---

## Quick Start

### Minimal API — JWT only

```csharp
using MicroKit.Security.Core.DependencyInjection;
using MicroKit.Security.Jwt.DependencyInjection;
using MicroKit.Security.Jwt.AspNetCore.DependencyInjection;
using MicroKit.Security.AspNetCore.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMicroKitSecurity(builder.Configuration)
    .AddJwt(builder.Configuration)
    .AddJwtAspNetCore(builder.Configuration);

var app = builder.Build();

app.UseMicroKitSecurity();

app.MapGet("/api/me", (IClientContextAccessor ctx) =>
    Results.Ok(new
    {
        UserId = ctx.Context!.Principal.Identifier,
        Name   = ctx.Context!.Principal.DisplayName,
        Tenant = ctx.Context!.TenantId
    }));

app.Run();
```

### Multiple providers

```csharp
builder.Services
    .AddMicroKitSecurity(builder.Configuration)
    .AddJwt(builder.Configuration)
    .AddJwtAspNetCore(builder.Configuration)
    .AddApiKey(builder.Configuration)
    .AddApiKeyAspNetCore(builder.Configuration);
```

---

## Package Reference

### MicroKit.Security.Abstractions

Zero-dependency contracts consumed by all other packages.

#### Core types

| Type | Kind | Description |
|------|------|-------------|
| `SecurityPrincipal` | `record` | Authenticated identity: `Identifier`, `DisplayName`, `TenantId`, `Claims` |
| `SecurityClaim` | `readonly record struct` | Immutable `(Type, Value)` pair — use `IsType()`, `Matches()` |
| `AnonymousPrincipal` | singleton | Unauthenticated principal — `IsAuthenticated = false`, empty claims |
| `SecurityAuthResult` | `record` | Outcome of an authentication attempt — `Success`/`Failure` factory methods |
| `ExtractionResult` | `record` | Raw credential extracted from an HTTP request |
| `IClientContext` | interface | Request-scoped context: principal, scheme, tenant, correlationId, timestamp |
| `IClientContextAccessor` | interface | `AsyncLocal`-backed accessor for the current `IClientContext` |
| `IAuthenticationProvider` | interface | Implement to add a custom authentication scheme |
| `IAuthorizationService` | interface | Evaluates role/permission/scope grants |
| `IApiKeyValidator` | interface | Validates an API key record against the incoming request context |
| `AuthenticationScheme` | `enum` | `None`, `ApiKey`, `Jwt`, `OAuth2`, `Cognito`, `AzureAd` |
| `AuthenticationMode` | `enum` | `FirstSuccess` (default) or `StrictSingleCredential` |
| `ApiKeyHashAlgorithms` | `enum` | `SHA256` or `SHA512` |

#### Claim types

```csharp
ClaimTypes.Subject      // "sub"
ClaimTypes.Email        // "email"
ClaimTypes.Name         // "name"
ClaimTypes.Role         // "role"
ClaimTypes.Roles        // "roles"
ClaimTypes.Permission   // "permission"
ClaimTypes.Permissions  // "permissions"
ClaimTypes.Scope        // "scope"
ClaimTypes.TenantId     // "tenant_id"
ClaimTypes.ClientId     // "client_id"
```

#### Extension methods (`SecurityPrincipalExtensions`)

```csharp
principal.HasRole("admin")
principal.HasAnyRole("admin", "editor")
principal.HasAllRoles("admin", "editor")
principal.GetRoles()              // IEnumerable<string>
principal.HasPermission("orders:read")
principal.GetEmail()
```

---

### MicroKit.Security.Core

Orchestration, caching, and authorization implementation.

#### Registration

```csharp
// Option A — code-first
builder.Services
    .AddMicroKitSecurity(options =>
    {
        options.RequireAuthenticatedUser = true;
        options.ExemptedPaths = ["/health", "/ready"];
        options.TenantIdHeader = "X-Tenant-ID";
        options.CorrelationIdHeader = "X-Correlation-ID";
        options.AuthenticationMode = AuthenticationMode.FirstSuccess;
        options.EnableAuditLogging = true;
    });

// Option B — configuration-bound
builder.Services.AddMicroKitSecurity(builder.Configuration);
// Reads from "MicroKit:Security" section

// Add distributed cache support (L2)
builder.Services
    .AddMicroKitSecurity(builder.Configuration)
    .WithDistributedCache(options =>
    {
        options.DefaultExpiration = TimeSpan.FromMinutes(10);
    });
```

#### `IClientContextAccessor`

```csharp
public class OrderService(IClientContextAccessor accessor)
{
    public async Task<Order> CreateAsync(CreateOrderRequest request)
    {
        var ctx = accessor.Context
            ?? throw new InvalidOperationException("Not authenticated");

        return new Order
        {
            UserId        = ctx.Principal.Identifier!,
            TenantId      = ctx.TenantId,
            CorrelationId = ctx.CorrelationId,
        };
    }
}
```

#### `IAuthorizationService`

```csharp
// Check any of the listed roles/permissions/scopes (OR logic)
bool allowed = authzService.IsAuthorized(principal, "admin", "editor");

// Require all permissions (AND logic)
bool full = authzService.HasAllPermissions(principal, "orders:read", "orders:write");
```

#### `SecureHasher`

```csharp
Span<char> dest = stackalloc char[64];
SecureHasher.TryComputeSha256("mk_live_xxx".AsSpan(), dest);

Span<char> dest512 = stackalloc char[128];
SecureHasher.TryComputeSha512("mk_live_xxx".AsSpan(), dest512);
```

---

### MicroKit.Security.AspNetCore

Middleware, extractors, attributes, and endpoint extensions.

#### Middleware

```csharp
// After app.Build(), before MapXxx
app.UseMicroKitSecurity();
```

`SecurityMiddleware` runs all registered `IAuthenticationExtractor` implementations, passes extracted credentials to `IAuthenticationService`, and sets the `IClientContext` on the `IClientContextAccessor`.

#### Endpoint extensions

```csharp
app.MapGet("/api/profile", handler).RequireAuthentication();
app.MapPost("/api/orders", handler).RequirePermissions("orders:write");
```

#### Attributes (MVC controllers)

```csharp
[RequirePermission("orders:read")]
public IActionResult GetOrders() { ... }
```

#### `HttpContextExtensions`

```csharp
var ctx       = httpContext.GetClientContext();
var principal = httpContext.GetSecurityPrincipal();
bool authed   = httpContext.IsAuthenticated();
string? tenant = httpContext.GetTenantId();
```

---

### MicroKit.Security.Jwt

JWT token validation and generation.

#### Registration

```csharp
builder.Services
    .AddMicroKitSecurity(builder.Configuration)
    .AddJwt(builder.Configuration)              // validates + generates tokens
    .AddJwtAspNetCore(builder.Configuration);   // extracts Bearer tokens from HTTP
```

#### Configuration (`appsettings.json`)

```json
{
  "MicroKit": {
    "Security": {
      "Jwt": {
        "Signing": {
          "Algorithm": "HS256",
          "SecretKey": "your-256-bit-secret-key-min-32-chars"
        },
        "Validation": {
          "Issuer": "https://auth.example.com",
          "Audience": "my-api",
          "ValidateIssuer": true,
          "ValidateAudience": true,
          "ValidateLifetime": true,
          "ClockSkewMinutes": 5,
          "AccessTokenExpirationMinutes": 60,
          "RefreshTokenExpirationDays": 7
        },
        "ClaimsMapping": {
          "UserIdClaim": "sub",
          "UserNameClaim": "name",
          "TenantIdClaim": "tid"
        }
      }
    }
  }
}
```

#### RSA signing (asymmetric)

```json
"Signing": {
  "Algorithm": "RS256",
  "PrivateKey": "-----BEGIN RSA PRIVATE KEY-----\n...",
  "PublicKey":  "-----BEGIN PUBLIC KEY-----\n..."
}
```

#### Remote JWKS (Azure AD, Cognito, OIDC providers)

```json
"Signing": {
  "Algorithm": "RS256",
  "JwksUri": "https://login.microsoftonline.com/{tenant}/discovery/v2.0/keys",
  "JwksKeyRefreshMinutes": 60
}
```

#### Token generation (`IJwtTokenService`)

```csharp
public class AuthController(IJwtTokenService jwt) : ControllerBase
{
    [HttpPost("token")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var principal = new SecurityPrincipal(
            req.UserId, req.DisplayName, req.TenantId,
            [new SecurityClaim(ClaimTypes.Role, req.Role)]);

        var result = await jwt.GenerateTokenAsync(principal);

        return Ok(new { result.Token, result.ExpiresAt });
    }
}
```

#### Enabling per-provider result caching

```csharp
.AddJwt(builder.Configuration, useCache: true)
```

---

### MicroKit.Security.ApiKey

API key authentication with pluggable storage and secure hashing.

#### Registration

```csharp
// With a custom IApiKeyStore
builder.Services
    .AddMicroKitSecurity(builder.Configuration)
    .AddApiKey<MyApiKeyStore>(builder.Configuration)
    .AddApiKeyAspNetCore(builder.Configuration);

// Or provide the store separately
builder.Services.AddSingleton<IApiKeyStore, DatabaseApiKeyStore>();
builder.Services
    .AddMicroKitSecurity(builder.Configuration)
    .AddApiKey(builder.Configuration)
    .AddApiKeyAspNetCore(builder.Configuration);
```

#### Redis-backed store

```csharp
dotnet add package MicroKit.Security.ApiKey.RedisStore
```

```csharp
builder.Services
    .AddMicroKitSecurity(builder.Configuration)
    .AddApiKey(builder.Configuration)
    .AddApiKeyAspNetCore(builder.Configuration);

builder.Services.AddSingleton<IApiKeyStore, RedisApiKeyStore>();
```

#### Configuration (`appsettings.json`)

```json
{
  "MicroKit": {
    "Security": {
      "ApiKey": {
        "HeaderName": "X-API-Key",
        "QueryParameterName": "api_key",
        "KeyPrefix": "mk_",
        "HashAlgorithm": "SHA256",
        "AllowQueryParameter": false
      }
    }
  }
}
```

#### Implementing `IApiKeyStore`

```csharp
public class DatabaseApiKeyStore(IDbConnection db) : IApiKeyStore
{
    public async ValueTask<ApiKeyRecord?> GetByHashAsync(
        string keyHash, CancellationToken ct = default)
        => await db.QuerySingleOrDefaultAsync<ApiKeyRecord>(
            "SELECT * FROM api_keys WHERE key_hash = @Hash AND revoked_at IS NULL",
            new { Hash = keyHash });

    public async ValueTask<bool> StoreAsync(ApiKeyRecord record, CancellationToken ct = default)
    {
        var affected = await db.ExecuteAsync(
            "INSERT INTO api_keys (id, key_hash, owner_id, tenant_id, created_at, expires_at) " +
            "VALUES (@Id, @KeyHash, @OwnerId, @TenantId, @CreatedAt, @ExpiresAt)", record);
        return affected > 0;
    }
}
```

---

### MicroKit.Security.AzureAd

Azure Active Directory / Microsoft Entra ID token validation via OIDC discovery.

#### Registration

```csharp
builder.Services
    .AddMicroKitSecurity(builder.Configuration)
    .AddAzureAd(builder.Configuration)
    .AddJwtAspNetCore(builder.Configuration);   // Bearer extractor is reused
```

#### Configuration (`appsettings.json`)

```json
{
  "MicroKit": {
    "Security": {
      "AzureAd": {
        "TenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
        "ClientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
        "Audience": "api://my-app-id",
        "ValidateIssuer": true,
        "ValidateLifetime": true,
        "ClockSkewMinutes": 5,
        "JwksKeyRefreshMinutes": 60,
        "TenantIdClaim": "tid",
        "UserIdClaim": "oid",
        "UserNameClaim": "name"
      }
    }
  }
}
```

The `MetadataAddress` (`https://login.microsoftonline.com/{TenantId}/v2.0/.well-known/openid-configuration`) and `Issuer` are derived automatically from `TenantId`.

Validated token claims are mapped to `SecurityPrincipal` using `UserIdClaim → Identifier`, `UserNameClaim → DisplayName`, and `TenantIdClaim → TenantId`.

---

### MicroKit.Security.Cognito

AWS Cognito User Pool token validation via JWKS discovery.

#### Registration

```csharp
builder.Services
    .AddMicroKitSecurity(builder.Configuration)
    .AddCognito(builder.Configuration)
    .AddJwtAspNetCore(builder.Configuration);
```

#### Configuration (`appsettings.json`)

```json
{
  "MicroKit": {
    "Security": {
      "Cognito": {
        "Region": "us-east-1",
        "UserPoolId": "us-east-1_xxxxxxxx",
        "ClientId": "xxxxxxxxxxxxxxxxxxxxxxxxxx",
        "UserIdClaim": "sub",
        "UserNameClaim": "cognito:username",
        "GroupsClaim": "cognito:groups"
      }
    }
  }
}
```

`JwksUri` (`https://cognito-idp.{Region}.amazonaws.com/{UserPoolId}/.well-known/jwks.json`) and `Issuer` are derived automatically. `cognito:groups` values are expanded into individual `"role"` claims on the resulting `SecurityPrincipal`.

---

### MicroKit.Security.MultiTenancy

Bridges `IClientContextAccessor` to `ITenantResolutionStrategy` so MicroKit.MultiTenancy resolves tenant from the authenticated principal rather than request headers.

#### Registration

```csharp
// Requires MicroKit.MultiTenancy.Abstractions
builder.Services.AddSecurityPrincipalTenantResolution();
```

This registers `SecurityPrincipalTenantResolutionStrategy` as the `ITenantResolutionStrategy`, which returns `IClientContextAccessor.Context?.TenantId`. Call this **after** `AddMicroKitSecurity`.

---

## Configuration Reference

Complete `appsettings.json` example:

```json
{
  "MicroKit": {
    "Security": {
      "RequireAuthenticatedUser": true,
      "AuthenticationMode": "FirstSuccess",
      "EnableAuditLogging": true,
      "CorrelationIdHeader": "X-Correlation-ID",
      "TenantIdHeader": "X-Tenant-ID",
      "ExemptedPaths": ["/health", "/ready", "/metrics", "/scalar", "/openapi"],
      "ClaimsMapping": {
        "RoleClaim": "role",
        "PermissionClaim": "permission",
        "ScopeClaim": "scope"
      },

      "Jwt": {
        "Signing": {
          "Algorithm": "HS256",
          "SecretKey": "your-256-bit-secret-key-min-32-chars"
        },
        "Validation": {
          "Issuer": "https://auth.example.com",
          "Audience": "my-api",
          "ClockSkewMinutes": 5,
          "AccessTokenExpirationMinutes": 60
        }
      },

      "ApiKey": {
        "HeaderName": "X-API-Key",
        "KeyPrefix": "mk_",
        "HashAlgorithm": "SHA256"
      },

      "AzureAd": {
        "TenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
        "ClientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
      },

      "Cognito": {
        "Region": "us-east-1",
        "UserPoolId": "us-east-1_xxxxxxxx",
        "ClientId": "xxxxxxxxxxxxxxxxxxxxxxxxxx"
      }
    }
  }
}
```

---

## Advanced Scenarios

### Custom authentication provider

```csharp
public sealed class ApiGatewayAuthProvider(IOptions<ApiGatewayOptions> options)
    : IAuthenticationProvider
{
    public AuthenticationScheme Scheme => AuthenticationScheme.None;

    public async ValueTask<SecurityAuthResult> AuthenticateAsync(
        string credential, CancellationToken ct = default)
    {
        var principal = await ValidateGatewayTokenAsync(credential, ct);
        if (principal is null)
            return SecurityAuthResult.Failure(ValidationStatus.Invalid, "Invalid gateway token");

        return SecurityAuthResult.Success(principal, Scheme);
    }
}

// Register
builder.Services.AddSingleton<IAuthenticationProvider, ApiGatewayAuthProvider>();
```

### Per-provider result caching

Both L1 (memory) and L2 (distributed) caching layers are controlled per provider:

```csharp
builder.Services
    .AddMicroKitSecurity(builder.Configuration)
    .WithDistributedCache()            // enables L2 for all providers that opt in
    .AddJwt(builder.Configuration, useCache: true)
    .AddApiKey(builder.Configuration, useCache: true);
```

Cache duration is controlled by the provider's `Cache` property (e.g., `JwtOptions.Cache.DefaultExpiration`).

### Strict single-credential mode

Reject requests that present more than one credential (prevents credential confusion attacks):

```json
{
  "MicroKit": {
    "Security": {
      "AuthenticationMode": "StrictSingleCredential"
    }
  }
}
```

### Multi-tenancy + Security bridge

```csharp
builder.Services
    .AddMicroKitSecurity(builder.Configuration)
    .AddJwt(builder.Configuration)
    .AddJwtAspNetCore(builder.Configuration);

// Resolve tenant from the authenticated principal instead of headers
builder.Services.AddSecurityPrincipalTenantResolution();

// MultiTenancy middleware reads ITenantResolutionStrategy
// which now delegates to IClientContextAccessor
```

---

## Best Practices

**Store secrets outside source control.** Use environment variables, Azure Key Vault, or AWS Secrets Manager for `SecretKey`, `PrivateKey`, and similar values.

**Never log credentials.** The middleware strips tokens from structured logs automatically, but ensure custom code does not echo raw `Authorization` header values.

**Use `RequireAuthenticatedUser = true` in production.** Opt-out individual endpoints via `ExemptedPaths` or the `[AllowAnonymous]` attribute.

**Enable caching for high-throughput APIs.** JWT validation involves cryptographic work; caching validated results for 5–10 minutes drastically reduces CPU under load:

```csharp
.AddJwt(builder.Configuration, useCache: true)
```

**Check `IsAuthenticated` before accessing principal data:**

```csharp
if (!accessor.Context?.IsAuthenticated ?? true)
    return Result.Unauthorized();

var userId = accessor.Context!.Principal.Identifier;
```

---

## Troubleshooting

### 401 on every request

1. Confirm `app.UseMicroKitSecurity()` is called before `app.MapXxx`.
2. Confirm the correct extractor package is installed (`MicroKit.Security.Jwt.AspNetCore` for Bearer tokens, `MicroKit.Security.ApiKey.AspNetCore` for API keys).
3. Check `ExemptedPaths` — the path might inadvertently match.

### "Token validation failed: issuer mismatch"

The `Issuer` in options must exactly match the `iss` claim in the token:

```json
"Validation": { "Issuer": "https://auth.example.com" }
```

### "Tenant mismatch" exception

`SecurityContextFactory` rejects requests where the JWT's `tid` claim and the `X-Tenant-ID` header contain different values. Either omit the header (the JWT tenant will be used) or ensure both match.

### API key not found

1. Confirm the store returns a record matching the SHA-256 hash of the raw key.
2. Confirm `KeyPrefix` in options matches the key format (e.g., `"mk_"`).
3. If using `RedisApiKeyStore`, verify the Redis connection is healthy.

### Debug logging

```csharp
builder.Logging.AddFilter("MicroKit.Security", LogLevel.Debug);
```

---

## License

MIT — see `LICENSE` for details.
