# Changelog — MicroKit.Auth

All notable changes to this project will be documented in this file.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versioning: [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0-preview.2] — 2026-06-28

### Changed
- `MicroKit.Auth.Multitenancy` now depends on `MicroKit.Tenancy.Abstractions`
  (was `MicroKit.Multitenancy.Abstractions`) following the rename of the
  MicroKit.Multitenancy module to MicroKit.Tenancy. No public API changes —
  all 9 Auth packages are republished so the package dependency graph on NuGet
  points at the renamed package.

## [1.0.0-preview.1]

### Added
- `MicroKit.Auth.Abstractions` — ICurrentUser, ISecurityContext, ICurrentUserAccessor,
  IPermissionChecker, ITenantPermissionChecker, IPermissionStore, IRoleChecker,
  IJwtValidator, IClaimsMapper, Permission and Role value objects
- `MicroKit.Auth` — CurrentUser, SecurityContext, AsyncLocal-backed CurrentUserAccessor,
  ClaimsMapper, permission evaluation engine, DI registration
- `MicroKit.Auth.AspNetCore` — authentication middleware, `[RequirePermission]` attribute,
  PermissionAuthorizationHandler, DI and application-builder extensions
- `MicroKit.Auth.Permissions` — permission definitions, registry, wildcard matching
- `MicroKit.Auth.Roles` — role definitions, inheritance, role → permission mapping
- `MicroKit.Auth.Jwt` — HS256 token validation and HMAC token generation (ADR-AUTH-007)
- `MicroKit.Auth.Supabase` — Supabase JWT + JWKS/ES256 claims-mapping adapter
- `MicroKit.Auth.Multitenancy` — `AuthTenantResolutionStrategy`, an additive
  `ITenantResolutionStrategy` (Order 40) that derives the current tenant from the
  authenticated user's identity claims
- `MicroKit.Auth.Testing` — FakeCurrentUser, FakeCurrentUserBuilder, FakePermissionChecker
