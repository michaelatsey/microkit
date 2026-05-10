# CLAUDE.md

# MicroKit — Architecture, Engineering Rules & Package Guidelines

Version: 1.0
Scope: Library Architecture, Package Design, Engineering Guidelines, NuGet Publishing
Project Type: .NET 10 Modular Library — Reusable Infrastructure & Domain Primitives

---

## IMPORTANT — REPOSITORY LAYOUT REFERENCE

The canonical package structure is defined in **STRUCTURE.md**.
AI assistants MUST read STRUCTURE.md as the authoritative layout reference before making any structural changes or generating new files.

---

## 1. PROJECT PURPOSE

MicroKit is a mature, opinionated, production-ready .NET 10 library suite providing reusable infrastructure and domain primitives for building SaaS platforms, modular monoliths, and distributed systems.

It is NOT:

- a framework
- a full application stack
- a tutorial project
- a proof of concept

It IS:

- a collection of focused, independently publishable NuGet packages
- infrastructure primitives that solve real production problems
- a library designed for senior engineers building serious systems
- a foundation for modular monolith and microservices architectures

---

## 2. CORE DESIGN PHILOSOPHY

### Package-first design

Every module is an independently publishable NuGet package.
A consumer should be able to install only what they need — no forced dependencies.

### Abstraction-first

Every module ships with an `Abstractions` package containing only interfaces and contracts.
Implementation packages depend on Abstractions — never the reverse.

### Zero vendor lock-in

Abstractions never reference third-party libraries.
Third-party integrations live in dedicated packages:
- `MicroKit.Cqrs.MediatR` — not in `MicroKit.Cqrs`
- `MicroKit.Messaging.Transport.RabbitMQ` — not in `MicroKit.Messaging.Core`
- `MicroKit.Caching.Distributed` — not in `MicroKit.Caching`

### Layered dependencies

```
Abstractions  ←  Core  ←  Integrations (MediatR, EFCore, Redis, RabbitMQ...)
```

No circular dependencies between packages. Ever.

### .NET 10 first

All packages target .NET 10.
No legacy compatibility layers.
Use the latest C# features where they improve clarity and safety.

---

## 3. RULE PRIORITY ORDER

1. **Package independence** — each package must be usable standalone
2. **Abstraction purity** — Abstractions packages have zero third-party dependencies
3. **No circular dependencies** — enforced at all times
4. **Backward compatibility** — public APIs are stable once published
5. **Minimal surface area** — expose only what consumers need

---

## 4. PACKAGE STRUCTURE

### Top-level modules

| Module | Purpose |
|---|---|
| `MicroKit.Abstractions` | Shared cross-cutting abstractions (markers, base interfaces) |
| `MicroKit.Core` | Core utilities shared across all modules |
| `MicroKit.Domain` | Domain primitives — Entity, AggregateRoot, ValueObject, Result, Error |
| `MicroKit.Cqrs` | CQRS abstractions and MediatR implementation |
| `MicroKit.Events` | Domain and Integration Event contracts |
| `MicroKit.Messaging` | Outbox/Inbox pattern — reliable message delivery |
| `MicroKit.Idempotency` | Idempotency enforcement — EFCore, Redis, MediatR |
| `MicroKit.Data` | Data access abstractions and EF Core base implementations |
| `MicroKit.EntityFrameworkCore` | EF Core extensions and base DbContext |
| `MicroKit.Caching` | Caching abstractions and distributed cache implementations |
| `MicroKit.MultiTenancy` | Multi-tenancy — tenant resolution, EFCore, Redis |
| `MicroKit.Resilience` | Resilience patterns — retry, circuit breaker, MediatR integration |
| `MicroKit.Security` | Authentication, authorization, API key, multi-tenancy security |
| `MicroKit.OpenApi` | OpenAPI / Scalar configuration helpers |
| `MicroKit.Payments` | Payment abstractions + Stripe implementation |

### Internal package structure (per module)

```
MicroKit.[Module]/
├── docs/
│   └── Readme.md              # Module-specific documentation
├── src/
│   ├── MicroKit.[Module].Abstractions/   # Interfaces only — zero third-party deps
│   ├── MicroKit.[Module]/                # Core implementation
│   └── MicroKit.[Module].[Integration]/ # Third-party integrations
└── tests/
    ├── MicroKit.[Module].Tests/
    └── MicroKit.[Module].Integration.Tests/
```

---

## 5. PACKAGE DESIGN RULES

### Abstractions packages

MUST contain ONLY:
- Interfaces
- Abstract base classes
- Marker interfaces
- Enums used in public contracts
- Record/struct types used in contracts

MUST NEVER contain:
- Concrete implementations
- Third-party library references
- Infrastructure concerns
- Framework-specific code

### Core/implementation packages

- Depend on their own Abstractions package
- May depend on `MicroKit.Core` and `MicroKit.Abstractions`
- MUST NOT depend on other MicroKit modules' implementation packages
- May depend on other MicroKit modules' Abstractions packages

### Integration packages (MediatR, EFCore, Redis, RabbitMQ, etc.)

- Named explicitly: `MicroKit.[Module].[Technology]`
- Depend on the module's Abstractions package
- Depend on the third-party library
- Contain ONLY the adapter/bridge code

---

## 6. DOMAIN MODULE RULES (`MicroKit.Domain`)

### Entity

- Has identity (`Id`)
- Mutable state through domain methods only
- Raises Domain Events via `AddDomainEvent()`

### AggregateRoot

- Extends Entity
- Single entry point for state changes
- Owns and raises all Domain Events
- External code interacts only with the AggregateRoot

### ValueObject

- No identity — equality by value
- Immutable — no setters
- Validation in constructor or factory method

### Result pattern

- `Result` and `Result<T>` for operation outcomes
- No exceptions for business flow control
- `Error` type with code and message

### Domain Events

- Implement `IDomainEvent`
- Named in past tense
- Raised inside aggregate methods
- Never published synchronously to external systems

---

## 7. CQRS MODULE RULES (`MicroKit.Cqrs`)

### Abstractions (`MicroKit.Cqrs.Abstractions`)

- `ICommand`, `ICommand<TResponse>`
- `IQuery<TResponse>`
- `ICommandBus`, `IQueryBus`
- `ICommandHandler<TCommand>`, `ICommandHandler<TCommand, TResponse>`
- `IQueryHandler<TQuery, TResponse>`

### MediatR decoupling

MediatR is an implementation detail — NEVER referenced in Abstractions.
`MicroKit.Cqrs.MediatR` provides:
- `MediatRCommandBus : ICommandBus`
- `MediatRQueryBus : IQueryBus`

### Behaviors (`MicroKit.Cqrs.MediatR.Behaviors`)

MediatR pipeline behaviors:
1. `LoggingBehavior`
2. `ValidationBehavior` — FluentValidation, fail fast
3. `PerformanceBehavior`
4. `TransactionBehavior` — commands only

---

## 8. MESSAGING MODULE RULES (`MicroKit.Messaging`)

### Outbox pattern

- `MicroKit.Messaging.Core` — Outbox/Inbox base implementation
- `MicroKit.Messaging.Persistence.EFCore` — EF Core persistence
- `MicroKit.Messaging.Persistence.Dapper` — Dapper persistence (read-optimized)
- `MicroKit.Messaging.Publisher.MediatR` — MediatR integration
- `MicroKit.Messaging.Transport.RabbitMQ` — RabbitMQ transport
- `MicroKit.Messaging.Transport.AzureSB` — Azure Service Bus transport

### Rules

- Outbox write MUST be atomic with aggregate save
- At-least-once delivery — consumers must be idempotent
- Transport is swappable — never coupled to business logic

---

## 9. IDEMPOTENCY MODULE RULES (`MicroKit.Idempotency`)

- Deduplication by `MessageId` or `RequestId`
- Storage backends: EFCore, Redis
- MediatR pipeline integration via `MicroKit.Idempotency.MediatR`
- Idempotency keys are immutable once stored

---

## 10. MULTI-TENANCY MODULE RULES (`MicroKit.MultiTenancy`)

- Tenant resolution is pluggable (header, JWT, subdomain)
- EF Core global query filters per tenant
- Redis tenant cache
- Never hardcode tenant resolution strategy in core

---

## 11. VERSIONING & PUBLISHING RULES

### Semantic Versioning

- `MAJOR` — breaking public API changes
- `MINOR` — new features, backward compatible
- `PATCH` — bug fixes

### NuGet Publishing

- Every package has its own `PackageVersion`
- `Directory.Build.props` defines shared metadata
- README per package in `docs/Readme.md`
- XML documentation enabled on all public APIs
- `<Nullable>enable</Nullable>` on all projects
- `<ImplicitUsings>enable</ImplicitUsings>` on all projects

### Pre-release

Use `-preview.N` suffix for unstable packages:
`1.0.0-preview.1`

---

## 12. CODE QUALITY RULES

- Nullable reference types enabled everywhere
- No `#pragma warning disable` without explicit justification comment
- No `public` methods without XML documentation
- No `catch (Exception)` without logging and rethrowing or explicit justification
- No static state in library code
- No `Thread.Sleep` — use `Task.Delay` with CancellationToken
- Prefer `IReadOnlyCollection<T>` over `List<T>` in public APIs
- Prefer records for immutable data transfer types
- Prefer `sealed` on classes not designed for inheritance

---

## 13. TESTING RULES

| Test Type | Tool | Scope |
|---|---|---|
| Unit | xUnit | Domain logic, handlers, behaviors |
| Integration | xUnit + Testcontainers | EFCore, Redis, messaging |
| Contract | xUnit | Public API surface stability |

Rules:
- Abstractions packages have unit tests only — zero infrastructure
- Integration packages tested against real infrastructure via Testcontainers
- Public API changes require contract test updates

---

## 14. WHAT IS IN SCOPE NOW

Focus on stabilizing these modules first (85% → 100%):

1. `MicroKit.Domain` — foundation for everything
2. `MicroKit.Cqrs` + `MicroKit.Cqrs.MediatR` — core dispatch
3. `MicroKit.Events` — domain + integration events
4. `MicroKit.Messaging` — outbox/inbox
5. `MicroKit.Idempotency` — reliable consumption
6. `MicroKit.Data` + `MicroKit.EntityFrameworkCore` — persistence
7. `MicroKit.MultiTenancy` — SaaS foundation
8. `MicroKit.Caching` — performance layer

Defer until stable:
- `MicroKit.Payments`
- `MicroKit.Resilience`
- `MicroKit.Security`
- `MicroKit.OpenApi`

---

## 15. WHAT MUST NEVER HAPPEN

- Abstractions packages reference third-party libraries
- Circular dependencies between packages
- Business logic in infrastructure packages
- Breaking public API changes without major version bump
- Packages published without XML documentation
- Static mutable state in any package
- Concrete implementations in Abstractions packages
- `MicroKit.Cqrs` directly referencing MediatR (use `MicroKit.Cqrs.MediatR`)

---

## 16. DECISION FILTER

Before adding any code, ask:

1. Does this belong in Abstractions or Implementation?
2. Does this introduce a circular dependency?
3. Is this a new third-party dependency that should be in its own package?
4. Does this break existing public APIs?
5. Is this solving a real production problem or is it over-engineering?
6. Is there a test covering this?
7. Would a senior .NET engineer immediately understand this?

If not: DO NOT ADD IT.