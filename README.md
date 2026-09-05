# MicroKit

Ecosystem of modular, opinionated .NET 10+ libraries for production-ready applications.

## Modules

| Module | Description | Status |
|--------|-------------|--------|
| MicroKit.Result | Railway-oriented result type | Preview on NuGet |
| MicroKit.MediatR | CQRS pipeline over MediatR | Preview on NuGet |
| MicroKit.Domain | DDD primitives | Preview on NuGet |
| MicroKit.Execution.Abstractions | Cross-cutting execution scope and context | Preview on NuGet |
| MicroKit.Messaging | Message bus + outbox + saga | Preview on NuGet |
| MicroKit.Persistence | Repository + UoW + EF/Dapper | Preview on NuGet |
| MicroKit.Caching | Distributed multi-layer cache | Planned |
| MicroKit.Http | Resilient HTTP clients | Planned |
| MicroKit.Auth | JWT + policies + identity | Preview on NuGet |
| MicroKit.Observability | OpenTelemetry + metrics + health | Planned |
| MicroKit.Logging | Structured logging | Preview on NuGet |
| MicroKit.Tenancy | Multi-tenancy support | Preview on NuGet |

**Preview on NuGet** — packages are published and in preview. **Planned** — nothing is written yet.

## Getting Started

A release is a declared set of packages sharing one version; membership in the build graph confers
no right to publish. Packages outside a given release keep the version they already carry, so a
shared version does not mean everything republishes. Every listed package is a preview today, so
`--prerelease` is required.

```bash
dotnet add package MicroKit.Result --prerelease
dotnet add package MicroKit.MediatR --prerelease
```
