# MicroKit.Auth.Jwt — Deferred Features

## Context
These features were explicitly deferred from Phase 1 (ADR-AUTH-007).
Read this file before implementing any Phase 2+ work on MicroKit.Auth.Jwt.

---

## Deferred

### Refresh Token
- `IJwtRefreshTokenProvider` — abstraction declared in Abstractions (Phase 1 contract only, no implementation)
- `IRefreshTokenStore` — storage abstraction (Phase 2)
- Rotation strategy: one-time use (Phase 2)

### Token Revocation
- `IJwtRevocationStore` — blacklist backed by Redis or DB
- Check in `JwtValidator` before returning `Success`

### Claims Enrichment Pipeline
- `IJwtClaimsEnricher` — inject custom claims before signing

### Key Management
- RSA/ECDSA support in `JwtOptions` (HMAC sufficient for Phase 1)
- `IJwtKeyProvider` — key rotation abstraction

### Token Introspection
- `IJwtIntrospector` — expose token metadata without full validation

### Null-Object Registration for IJwtRefreshTokenProvider
- `IJwtRefreshTokenProvider` is declared as a Phase 1 contract-only interface with no implementation.
- Unlike `IPermissionStore`, `IRoleStore`, and `IRolePermissionMap`, no null-object implementation
  is registered by `AddMicroKitAuthJwt()`. Consumers who accidentally inject
  `IJwtRefreshTokenProvider` from DI will receive a `InvalidOperationException` rather than a
  graceful no-op — which is the correct behaviour for an unimplemented contract.
- Phase 2 work: add `NullJwtRefreshTokenProvider` (returns `NotImplementedError` from `IssueAsync`
  and `ExchangeAsync`) and register it via `TryAddSingleton` in `AddMicroKitAuthJwt()` for
  consistency with the null-object pattern used elsewhere in the module.
