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

| Algorithm | Support | Notes |
|-----------|---------|-------|
| RS256 | ✅ Required | Standard for OIDC providers |
| ES256 | ✅ Required | Supabase default |
| HS256 | ⚠️ Optional | Only for internal/dev scenarios |

---

## JWKS Strategy

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

## Forbidden

```
❌ Token generation (signing, issuing) — belongs to identity provider
❌ Refresh token handling — belongs to identity provider
❌ Token storage — stateless validation only
❌ Exception thrown on invalid token — always Result<T>.Failure
❌ Hardcoded signing keys in source code
```
