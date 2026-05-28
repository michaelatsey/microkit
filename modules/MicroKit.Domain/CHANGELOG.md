# Changelog — MicroKit.Domain

All notable changes to this project will be documented in this file.

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
Versioning: [Semantic Versioning](https://semver.org/)

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
