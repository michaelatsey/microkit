# Rule: Abstractions Minimality — MicroKit.Persistence

## Core principle (ADR-003)
`MicroKit.Persistence.Abstractions` contains **only what a consuming module needs to compile**.
If a type is not required to declare a handler signature or a service registration, it does not
belong in Abstractions.

## What belongs in Abstractions

```csharp
// ✅ Repository contracts
IRepository<TAggregate>        // #196 phase 2: replaced by IRepository<TAggregate, TId> (D10)
IReadRepository<TAggregate>    // empty marker — #196 phase 2 deletes it; the full contract is in Core (D8)

// ✅ Unit of Work — the single commit boundary
IUnitOfWork

// ✅ Transaction contracts
ITransactionalContext
ITransaction, ITransactionManager   // used nowhere — #196 phase 2 deletes them (D12)

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
<!-- cross-module, two-ItemGroup CIReleaseBuild pattern -->
<PackageReference Include="MicroKit.Domain" />
<PackageReference Include="MicroKit.Result" />   <!-- removed by #196 D17 (phase 2) -->
```

`MicroKit.Domain` is required for the `IAggregateRoot` constraint on the repository's generic
parameters. `MicroKit.Result` is used by nothing but a `global using`; #196 D17 removes it in
phase 2. No other non-BCL package reference is allowed in Abstractions.

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
