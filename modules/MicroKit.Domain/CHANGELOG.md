# Changelog — MicroKit.Domain

All notable changes to this project will be documented in this file.

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
Versioning: [Semantic Versioning](https://semver.org/)

## [Unreleased]

Settles the public surface before `2.0.0` (#160). Every item under **Breaking** is a break on
`preview.*` (ADR-GLOBAL-002 D10); the reasoning is in
`docs/architecture/ADR-003-Domain-Surface-Before-Stable.md`.

### Breaking
- `IRepository<T, TId>`, `IReadRepository<T, TId>` and `IUnitOfWork` are removed with the
  `MicroKit.Domain.Repositories` namespace. Use `MicroKit.Persistence.Abstractions` (D1; #117,
  #118, #119).
- `IEntityId` loses `object Value` and becomes a marker; identifier types drop their explicit
  `IEntityId.Value` implementation (D2).
- `GuidId.Empty` is removed (D2).
- `Entity<TId>` throws `ArgumentException` for an identifier equal to `default(TId)` (D2; #111).
- `Entity<TId>` equality admits a runtime subclass with the same identifier, such as an EF Core
  proxy, instead of requiring the exact same type (D3).
- `AuditableEntity<TId>` and `AuditableAggregateRoot<TId>` lose their `(TId id)` constructor. They
  take `(id, DateTimeOffset createdAt, Actor createdBy)` or `(id, TimeProvider, Actor createdBy)`
  (D4; #112).
- `IAuditableEntity.CreatedBy` is a non-null `Actor`, and `UpdatedBy` an `Actor?`, instead of
  `string?` (D5; #116).
- `UpdatedAt` and `UpdatedBy` are `private set`; derived types call
  `Touch(DateTimeOffset, Actor)` (D5; #113).
- `DomainEvent` is `abstract record DomainEvent(DateTimeOffset OccurredAt)`: `OccurredAt` has no
  default and concrete events forward it positionally (D4; #115).
- `DomainEvent.EventId` defaults to `Guid.CreateVersion7()` instead of `Guid.NewGuid()` (D6; #114).
- `GuidId.New()` returns a UUID version 7 (D6).
- `Money` holds a `Currency` instead of a `string`, rejects unknown ISO 4217 codes and amounts
  finer than the minor unit; `Money.Zero` takes a `Currency` (D7).
- `Money.Multiply(decimal)` becomes `Multiply(decimal, MidpointRounding)` and rounds to the minor
  unit; the `*` operators are removed (D7).
- `Money.ToString()` formats with the invariant culture and the currency's minor unit instead of
  `F2` in the current culture (D7).
- `Specification<T>.IsSatisfiedBy` is no longer abstract and cannot be overridden; it evaluates
  `ToExpression()`. Derived specifications remove their `IsSatisfiedBy` override (D8).
- `IBusinessRule` gains `string Code`, abstract on `BusinessRule`; every rule implements it.
  `BusinessRuleViolationException` names the rule by `Code` in its message and rejects a rule
  whose `Code` is null or empty (D9).

### Added
- `Actor` value object, with the built-ins `Actor.System`, `Actor.Anonymous` and
  `Actor.Migration` (`$system`, `$anonymous`, `$migration`), and `Actor.Restore` to read a stored
  value back (D5).
- `Currency` value type, from the active codes of ISO 4217 List One (D7).
- `Money.Allocate(params int[] ratios)` (D7).
- `BusinessRuleViolationException.Code` (D9).
- Architecture tests: no MicroKit, EF Core, ASP.NET Core or DI dependency; sealed value objects;
  record domain events (D10).

### Removed
- The empty `MicroKit.Domain.IntegrationTests` and `MicroKit.Domain.PerformanceTests` projects,
  and the README-only `samples/` directories (D10).

### Fixed
- XML documentation of the `Entity` and `Auditable*` constructors explains EF Core constructor
  binding (#121); `IEvent` no longer cites the rejected `IApplicationEvent` (#153) (D11).

## [1.0.0-preview.1] — 2026-05-28

### Added
- `AggregateRoot<TId>`, `Entity<TId>` base types with ID-based equality and domain event support
- `IDomainEvent` contract and `DomainEvent` abstract base (immutable `sealed record`)
- `AuditableAggregateRoot<TId>` with `CreatedAt` / `UpdatedAt` tracking
- `IValueObject` marker interface; `sealed record` and `readonly record struct` value object strategy
- Common reusable value objects: `Money`, `Email`, `Address`, `DateRange`, `Percentage`
- `ISpecification<T>` and `Specification<T>` with composable `And` / `Or` / `Not` operators
- `IRepository<T, TId>`, `IReadRepository<T, TId>`, `IUnitOfWork` abstractions
- `IDomainService` marker interface
- `IBusinessRule` / `BusinessRule` with `CheckRule` enforcement
- `DomainException`, `BusinessRuleViolationException`, `EntityNotFoundException<T>` exceptions
- `IEntityId`, strongly-typed ID via `readonly record struct` pattern
- Benchmark infrastructure for all domain primitives

### Changed
- Value objects modernised to .NET 10 / C# 14 — `sealed record` replaces abstract `ValueObject` base class
- Test assertions migrated from FluentAssertions to Shouldly (MIT)
