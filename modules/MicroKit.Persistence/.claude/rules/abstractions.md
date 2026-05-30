# Rule: Abstractions Minimality — MicroKit.Persistence

## Core principle (ADR-003)
`MicroKit.Persistence.Abstractions` contains **only what a consuming module needs to compile**.
If a type is not required to declare a handler signature or a service registration, it does not
belong in Abstractions.

## What belongs in Abstractions

```csharp
// ✅ Repository contracts
IRepository<TAggregate>
IReadRepository<TAggregate>

// ✅ Unit of Work — the single commit boundary
IUnitOfWork

// ✅ Transaction contracts
ITransactionalContext
ITransaction
ITransactionManager

// ✅ Result contract
IPagedResult<T>

// ✅ Exception
PersistenceException

// ✅ Nothing else
```

## What does NOT belong in Abstractions

```
❌ ISpecificationEvaluator          → Core (infrastructure plumbing)
❌ QueryOptions<T>                  → Core (loading strategy — not needed for type signatures)
❌ EfRepository<T>, EfUnitOfWork   → EntityFrameworkCore (implementations, never in contracts)
❌ DbContext, DbSet<T>, IQueryable  → never in Abstractions
❌ Microsoft.EntityFrameworkCore    → not even as a package reference
❌ Npgsql.*, SqlServer.*            → provider-specific, never in Abstractions
❌ NSubstitute, Shouldly            → test packages, never in production code
❌ Any MicroKit.Persistence.* project reference (package refs only)
```

## Allowed package references in Abstractions

```xml
<!-- ✅ only these two -->
<PackageReference Include="MicroKit.Result" />
<PackageReference Include="MicroKit.Domain.Abstractions" />
```

`MicroKit.Result` is allowed because `IRepository` methods return `Result<T>` in consumer handlers.
`MicroKit.Domain.Abstractions` is required for `IAggregateRoot` constraint on generic repository parameters.
No other non-BCL package reference is allowed in Abstractions.

## The minimality test

Ask: "Could a consuming module that does NOT use EF Core reference this package and compile?"
If the answer is NO because of an EF Core type leak, the type is in the wrong package.

## Detecting violations

```bash
# EF Core leaked into Abstractions
grep -rn 'EntityFrameworkCore\|DbContext\|DbSet\|IQueryable\|ModelBuilder' \
  modules/MicroKit.Persistence/src/MicroKit.Persistence.Abstractions/ --include="*.cs" --include="*.csproj"

# Non-BCL package refs in Abstractions .csproj
grep 'PackageReference' \
  modules/MicroKit.Persistence/src/MicroKit.Persistence.Abstractions/MicroKit.Persistence.Abstractions.csproj
```
