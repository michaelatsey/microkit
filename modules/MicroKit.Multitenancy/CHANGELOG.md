# Changelog — MicroKit.Multitenancy

All notable changes to this project will be documented in this file.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versioning: [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `MicroKit.Multitenancy.Abstractions` — ITenantContext, ITenantContextAccessor, ITenantInfo, TenantId VO, ITenantResolver, ITenantResolutionStrategy, ITenantStore, ITenantProvisioner, ITenantEntity
- `MicroKit.Multitenancy` — AsyncLocalTenantContextAccessor, TenantResolutionPipeline, InMemoryTenantStore, DI registration
- `MicroKit.Multitenancy.AspNetCore` — TenantResolutionMiddleware, HTTP strategies (header, route, subdomain, claims, host)
- `MicroKit.Multitenancy.EntityFrameworkCore` — global query filter, TenantStampInterceptor, IgnoreTenantScope
- `MicroKit.Multitenancy.Analyzers` — MKT001, MKT002, MKT003 Roslyn diagnostics
