# microkit-auth-jwt

## Core Principle

JWT validation never throws. Always returns `Result<ClaimsPrincipal>`.

```csharp
// ✅ Required
ValueTask<Result<ClaimsPrincipal>> ValidateAsync(string token, CancellationToken ct = default);

// ❌ Forbidden
ClaimsPrincipal Validate(string token); // throws on failure
```

---

## Supported Algorithms

> **Phase 1 scope (ADR-AUTH-007):** `MicroKit.Auth.Jwt` supports **HS256 only**.
> RS256/ES256 via JWKS are implemented in provider packages (`MicroKit.Auth.Supabase`,
> `MicroKit.Auth.OpenIdConnect`), not in this base package.

| Algorithm | Package | Phase |
|-----------|---------|-------|
| HS256 | `MicroKit.Auth.Jwt` | Phase 1 ✅ |
| ES256 (JWKS) | `MicroKit.Auth.Supabase` | Phase 1 ✅ |
| RS256 (JWKS) | `MicroKit.Auth.OpenIdConnect` | Phase 2 📋 |

---

## Phase 1 Options (`MicroKit.Auth.Jwt`)

```csharp
public sealed record JwtOptions
{
    public string Issuer { get; init; }
    public string Audience { get; init; }
    public string Secret { get; init; }        // HMAC secret, minimum 32 characters
    public TimeSpan Expiry { get; init; }       // default: 1 hour
    public TimeSpan ClockSkew { get; init; }    // default: 5 minutes
}
```

---

## JWKS Strategy (Phase 2+ — provider packages only)

> Not applicable to `MicroKit.Auth.Jwt`. Applies to `Supabase` and `OpenIdConnect` packages.

- Remote JWKS endpoint fetched on startup + cached
- Key rotation: automatic refresh on `kid` mismatch (max 1 refresh per 5 min)
- JWKS URL configurable per provider
- Never hardcode signing keys

```csharp
public sealed class JwtValidationOptions
{
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required Uri JwksUri { get; init; }
    public TimeSpan JwksCacheDuration { get; init; } = TimeSpan.FromMinutes(60);
    public bool ValidateLifetime { get; init; } = true;
    public bool ValidateAudience { get; init; } = true;
}
```

---

## Supabase Specifics

Supabase JWT structure:
```json
{
  "sub": "uuid",
  "email": "user@example.com",
  "role": "authenticated",
  "app_metadata": { "provider": "email" },
  "user_metadata": { ... },
  "aud": "authenticated",
  "iss": "https://{project}.supabase.co/auth/v1"
}
```

Claims mapping to `ICurrentUser`:
```
sub           → UserId
email         → Email
role          → mapped to MicroKit Role via RoleMapper
user_metadata → custom claims (TenantId, Permissions)
```

Supabase JWKS URL pattern:
```
https://{project-ref}.supabase.co/auth/v1/.well-known/jwks.json
```

---

## Claims Mapping Contract

Every provider implements `IClaimsMapper`:

```csharp
public interface IClaimsMapper
{
    ICurrentUser MapFromClaims(ClaimsPrincipal principal);
    IEnumerable<Claim> MapToClaims(ICurrentUser user);
}
```

Rules:
- `sub` claim → always `UserId` (required, never null)
- `email` claim → `Email` (optional, nullable)
- `TenantId` → resolved from custom claim or header (via `MicroKit.Multitenancy`)
- Missing required claims → `Result.Failure` with descriptive error

---

## Token Generation (scoped exception — ADR-AUTH-007)

`MicroKit.Auth.Jwt` **may** generate HMAC-signed tokens via `IJwtTokenGenerator` / `JwtTokenGenerator`
for internal service-to-service use. This is a scoped exception to the general "validation only" rule.

Constraints that apply to this exception:
- Input must be `ICurrentUser` — no raw user/password, no session state
- HMAC key only (`Secret` from `JwtOptions`) — no asymmetric key generation
- No lifecycle management (no refresh, no revocation, no storage)

All **other** packages (`Core`, `AspNetCore`, `Supabase`, `OpenIdConnect`, all Phase 3 providers)
retain the original prohibition: they must never generate tokens.

---

## Forbidden

```
❌ Token generation in any package other than MicroKit.Auth.Jwt
❌ Refresh token implementation (deferred to Phase 2 — IJwtRefreshTokenProvider contract only)
❌ Token storage — stateless validation only
❌ Exception thrown on invalid token — always Result<T>.Failure
❌ Hardcoded signing keys in source code
❌ JWKS fetching in MicroKit.Auth.Jwt (deferred — belongs in provider packages)
```
