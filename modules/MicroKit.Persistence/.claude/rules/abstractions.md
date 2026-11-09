# Rule: Abstractions Minimality — MicroKit.Persistence

## Core principle (ADR-003)

`MicroKit.Persistence.Abstractions` contains only what a consuming module needs to compile against
the write side and the commit boundary. A type that is not needed to declare a handler signature
or a service registration does not belong here.

## What is in Abstractions — and nothing else

```csharp
IRepository<TAggregate, TId>   // TAggregate : class, IAggregateRoot; TId : IEntityId
IUnitOfWork                    // CommitAsync + DiscardChanges
ITransactionalContext          // the two ExecuteAsync overloads
IPagedResult<T>
PersistenceException
```

## What does NOT belong in Abstractions

```
❌ IReadRepository<T>              → core (its methods take QueryOptions<T>)
❌ ISpecificationEvaluator, QueryOptions<T>, PaginationOptions, PagedResult<T> → core
❌ ITransactionalUnitOfWork, EfRepository, EfUnitOfWork → EntityFrameworkCore (ADR-004)
❌ DbContext, DbSet<T>, IQueryable<T>, ModelBuilder → never
❌ Microsoft.EntityFrameworkCore, Npgsql.*, SqlServer.* → not even as a package reference
❌ NSubstitute, Shouldly          → test packages
❌ Any MicroKit.Persistence.* project reference
```

## Allowed references

`MicroKit.Domain` only — `IAggregateRoot` and `IEntityId` constrain `IRepository`'s type
parameters. It is an unconditional `ProjectReference`, from source in every build mode (#197 Spec,
Perimeter; `rules/dependencies.md`). No other non-BCL reference.

## The minimality test

"Could a module that does not use EF Core reference this package and compile?" If not, a type is
in the wrong package.

## Detecting violations

```bash
grep -rn 'EntityFrameworkCore\|DbContext\|DbSet\|IQueryable\|ModelBuilder' \
  modules/MicroKit.Persistence/src/MicroKit.Persistence.Abstractions/ --include="*.cs" --include="*.csproj"
grep -n 'Reference' \
  modules/MicroKit.Persistence/src/MicroKit.Persistence.Abstractions/MicroKit.Persistence.Abstractions.csproj
```

`LayerDependencyTests` and `ContractPlacementTests` (ArchitectureTests) enforce the same placement.
