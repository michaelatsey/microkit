# MicroKit

Ecosystem of modular, opinionated .NET 10+ libraries for production-ready applications.

## Modules

| Module | Description | Status |
|--------|-------------|--------|
| MicroKit.Result | Railway-oriented result type | Done |
| MicroKit.MediatR | CQRS pipeline over MediatR | In progress |
| MicroKit.Domain | DDD primitives | Done |
| MicroKit.Messaging | Message bus + outbox + saga | Planned |
| MicroKit.Persistence | Repository + UoW + EF/Dapper | Planned |
| MicroKit.Caching | Distributed multi-layer cache | Planned |
| MicroKit.Http | Resilient HTTP clients | Planned |
| MicroKit.Auth | JWT + policies + identity | Planned |
| MicroKit.Observability | OpenTelemetry + metrics + health | Planned |
| MicroKit.Logging | Structured logging | Planned |
| MicroKit.Multitenancy | Multi-tenancy support | Planned |

## Getting Started

Each module is independently versioned and published on NuGet.

```bash
dotnet add package MicroKit.Result
dotnet add package MicroKit.MediatR
```

## Contributing

See [docs/guides/contributing.md](docs/guides/contributing.md).
