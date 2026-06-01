# Context: Architectural Decisions

**ADR (Architecture Decision Records) for MicroKit.Persistence.**

Format: `## ADR-{NNN}: {Title}` · Status: `Accepted` | `Proposed` | `Superseded` | `Deprecated`

---

## ADR-001: IUnitOfWork Moved from MicroKit.Domain to MicroKit.Persistence.Abstractions

**Status:** Accepted
**Date:** 2026-05-30

### Decision

`IUnitOfWork` is declared in `MicroKit.Persistence.Abstractions`, **not** in `MicroKit.Domain`.
Any pre-existing code that imported `IUnitOfWork` from `MicroKit.Domain` must update its `using`
directive to `MicroKit.Persistence.Abstractions`.

### Rationale

1. **IUnitOfWork is infrastructure, not domain.** The Unit of Work pattern defines a transactional
   boundary over a persistence mechanism — a database, an event store, or an in-memory structure.
   This is an infrastructure concern. The domain layer (aggregates, value objects, domain events)
   should have no knowledge of how persistence works or when changes are committed.
2. **Avoiding circular-ish coupling.** If `MicroKit.Domain` declares `IUnitOfWork` and
   `MicroKit.Persistence.Abstractions` depends on `MicroKit.Domain.Abstractions` (for
   `IAggregateRoot`), then `IUnitOfWork` lives at Level 0. But the *implementation* of that contract
   (`EfUnitOfWork`) lives at Level 2. This is fine for the interface, but it puts a persistence
   concept in the domain layer, which erodes the clean separation between domain and infrastructure.
3. **Aligning with Evans and Fowler.** Evans (DDD Blue Book) places the Repository and UoW patterns
   in the Infrastructure layer. The domain layer defines the *need* for persistence (aggregates have
   state that must survive process restarts), but the mechanism is entirely infrastructure.
4. **Colocation with consumers.** The primary consumer of `IUnitOfWork.CommitAsync()` is a command
   handler registered in the application layer, which already depends on `MicroKit.Persistence.*`.
   Placing `IUnitOfWork` in `Persistence.Abstractions` means a single dependency satisfies both
   the repository contract and the commit boundary.

### Breaking Change

This is a **breaking change** for any code that imports `IUnitOfWork` from `MicroKit.Domain`.

**Migration:**
```csharp
// Before (MicroKit.Domain ≤ 1.0.0-preview.1)
using MicroKit.Domain;
// IUnitOfWork was here

// After (MicroKit.Domain ≥ 1.1.0 + MicroKit.Persistence.Abstractions ≥ 1.0.0)
using MicroKit.Persistence.Abstractions;
// IUnitOfWork is now here
```

`MicroKit.Domain` **does not** retain a forwarding type alias — clean cut, documented in CHANGELOG.

### What Stays in MicroKit.Domain

`MicroKit.Domain` retains all domain contracts:
- `IAggregateRoot`
- `IDomainEvent` / `IEvent`
- `Specification<T>` (predicate only — no infrastructure concern)
- Value object base types
- Domain exception types

### Consequences

- All existing users of `IUnitOfWork` in domain-layer code must be reconsidered — if a domain type
  (aggregate, value object, domain service) was injecting `IUnitOfWork`, that is itself a violation
  of domain purity and should be refactored.
- `MicroKit.MediatR.Behaviors.TransactionBehavior` injects `ITransactionalContext` (from
  `MicroKit.Persistence.Abstractions`) rather than `IUnitOfWork`, because the behavior manages
  the transaction lifecycle, not just a single commit point.

---

## ADR-002: QueryOptions Separates WHAT from HOW

**Status:** Accepted
**Date:** 2026-05-30

### Decision

`Specification<T>` (MicroKit.Domain) expresses **what** to query (predicate / criteria).
`QueryOptions<T>` (MicroKit.Persistence Core) expresses **how** to execute it (includes, tracking,
pagination, split queries). The two types are separate and never merged.

### Rationale

1. **Specification purity.** `Specification<T>` is a domain object. It must be testable without
   EF Core, without `IQueryable`, and without any infrastructure dependency. The moment a spec
   contains `.Include()` or pagination, it pulls in EF Core concepts.
2. **Reusability.** The same `ActiveUserSpec` can be used with:
   - `QueryOptions<User>(new ActiveUserSpec())` for a paged list with includes
   - `new QueryOptions<User>(new ActiveUserSpec())` with no includes for an `AnyAsync` check
   - A pure in-memory LINQ filter in unit tests: `spec.Criteria.Compile()(user)`
3. **Layer clarity.** The loading strategy (which navigations to load, whether to track, whether to
   paginate) is a concern of the **application layer** (the query handler), not the domain.
4. **EfSpecificationEvaluator as the adapter.** The evaluator applies `QueryOptions<T>` to an
   `IQueryable<T>` inside the EF Core project. No specification class ever touches `IQueryable`.

### Consequences

- `Specification<T>` in `MicroKit.Domain` contains only `AddCriteria(Expression<Func<T, bool>>)`.
  Any attempt to add `AddInclude()`, `ApplyPaging()`, or similar to `Specification<T>` is a violation.
- `QueryOptions<T>` is assembled in the query handler, not in domain services.
- The evaluator's application order is canonical (criteria → includes → split → order → paginate).
  See `.claude-context/standards/query-options.md`.

---

## ADR-003: Abstractions Minimality Rule

**Status:** Accepted
**Date:** 2026-05-30

### Decision

`MicroKit.Persistence.Abstractions` contains **only what a consuming module needs to compile**.
Specifically:
- Repository interfaces (`IRepository<T>`, `IReadRepository<T>`)
- Unit of Work (`IUnitOfWork`)
- Transactional context (`ITransactionalContext`, `ITransaction`, `ITransactionManager`)
- Paged result contract (`IPagedResult<T>`)
- Exception type (`PersistenceException`)

NOT in Abstractions:
- `ISpecificationEvaluator` (infrastructure plumbing)
- `QueryOptions<T>` (loading strategy — application concern)
- `EfRepository<T>`, `EfUnitOfWork` (implementations)
- Any EF Core type

### Rationale

1. **Consumer flexibility.** A module that depends on `Persistence.Abstractions` to register a
   typed repository in DI must be able to compile without an EF Core dependency. This is especially
   important for test projects that use `InMemoryRepository<T>` from `Persistence.Testing`.
2. **Abstraction stability.** Abstractions change rarely; implementations change often. The smaller
   the Abstractions surface, the less often consumers are forced to update.
3. **The minimality test:** "Could a consuming module that does NOT use EF Core reference this
   package and compile?" If the answer involves EF Core types, the type is in the wrong project.

### Consequences

- `ISpecificationEvaluator` lives in `MicroKit.Persistence` (Core), not Abstractions.
- `QueryOptions<T>` lives in Core.
- Consumers that want `QueryOptions<T>` depend on Core, not just Abstractions — this is expected
  and acceptable (most consumers will want both).
- The `dependency-check` hook and `dependency-guardian` agent enforce this automatically.

---

## ADR-004: ITransactionalUnitOfWork Is the EF Core Composite (Not in Abstractions)

**Status:** Accepted
**Date:** 2026-05-30

### Decision

`ITransactionalUnitOfWork : IUnitOfWork, ITransactionalContext` is declared in
`MicroKit.Persistence.EntityFrameworkCore`, **not** in `Persistence.Abstractions`.

### Rationale

1. **Composite is EF-specific.** `ITransactionalUnitOfWork` exists because a single `DbContext`
   naturally implements both commit (`SaveChangesAsync`) and transaction management
   (`Database.BeginTransactionAsync`). A non-EF persistence provider may not share this coupling.
2. **Abstractions must remain provider-agnostic.** The composite interface makes the Abstractions
   package EF-aware if placed there. Other providers (Marten, Dapper) would implement `IUnitOfWork`
   and `ITransactionalContext` separately, not as a composite.
3. **Consumers need only the individual interfaces.** The `TransactionBehavior` in MediatR.Behaviors
   injects `ITransactionalContext`. The handler injects `IUnitOfWork`. Neither needs `ITransactionalUnitOfWork`.
   The composite is only needed for the DI registration that maps both interfaces to the single
   `EfUnitOfWork` scoped instance.

### Consequences

- DI registration uses the triple-registration pattern (see `.claude-context/standards/transaction-contracts.md`).
- `ITransactionalUnitOfWork` is not part of the published API in Abstractions — it is an
  implementation detail of the EF Core integration.
- If a future `Marten` or `Dapper` provider is added, it registers its own scoped service implementing
  `IUnitOfWork` + `ITransactionalContext` without needing a composite interface.

---

## DN-001: Execution Strategy Coverage Gap in EfUnitOfWork.CommitAsync

**Status:** Deferred (v1.1 candidate)
**Date:** 2026-05-31
**Source:** Architect pre-release review — NOTE 7

### Observation

`EfUnitOfWork.ExecuteAsync<TState>` wraps its operation in `context.Database.CreateExecutionStrategy()`
providing automatic retry on transient failures (e.g., Azure SQL connection drops, Npgsql transient
errors). `EfUnitOfWork.CommitAsync` (plain `SaveChangesAsync`) does **not** use an execution strategy.

### When This Matters

| Call path | Protected? |
|-----------|------------|
| `TransactionBehavior` → `ExecuteAsync` → handler → `CommitAsync` | ✅ Yes — outer strategy covers it |
| Command handler calling `IUnitOfWork.CommitAsync` directly (no `TransactionBehavior`) | ❌ No retry |

The main command path (through `MicroKit.MediatR.Behaviors.TransactionBehavior`) is fully protected.
Direct `CommitAsync` calls from handlers that bypass `TransactionBehavior` have no transient-failure
retry. This is acceptable for localhost and on-premise databases; it is a gap for cloud-hosted
databases (Azure SQL, Amazon RDS, Neon) where transient failures occur regularly.

### Decision (Deferred)

This is not a correctness problem for v1.0.0-preview.1 — the primary command path is covered.
For v1.1.0, wrap `CommitAsync` in an execution strategy to provide full resilience for all callers:

```csharp
public async ValueTask CommitAsync(CancellationToken ct = default)
{
    var strategy = context.Database.CreateExecutionStrategy();
    await strategy.ExecuteAsync(async () =>
    {
        try { await context.SaveChangesAsync(ct).ConfigureAwait(false); }
        catch (DbUpdateConcurrencyException ex) { throw new PersistenceException("...", ex); }
        catch (DbUpdateException ex) { throw new PersistenceException("...", ex); }
    }).ConfigureAwait(false);
}
```

Note: this changes `CommitAsync` from being retry-safe only inside `ExecuteAsync` to being
independently retry-safe. The two strategies must not be nested — if `TransactionBehavior`'s
`ExecuteAsync` is already running, EF Core will throw on strategy nesting with explicit transactions.
A guard is required: skip the inner strategy when a transaction is already active.

---

## DN-002: ListPagedAsync Fallback PageSize When totalCount Is Zero

**Status:** Deferred (v1.1 candidate)
**Date:** 2026-05-31
**Source:** Architect pre-release review — NOTE 9

### Observation

`EfReadRepository.ListPagedAsync` (line 124) uses this fallback when `Pagination` is not specified:

```csharp
var pagination = opts.Pagination ?? new PaginationOptions(
    Page: 1,
    PageSize: totalCount > 0 ? totalCount : 1
);
```

When the result set is empty (`totalCount == 0`), `PageSize` is set to `1`. The paging math is
correct (`TotalPages = 0/1 = 0`), but a consumer inspecting the returned `IPagedResult<T>` will see
`PageSize = 1`, which may be misread as "only 1 item per page was requested."

### Risk

Low. The only consumer impacted is one that reads `IPagedResult.PageSize` from an unpaginated call
and treats it as the requested page size. In practice, callers that want unpaginated results are
unlikely to inspect `PageSize` at all.

### Decision (Deferred)

For v1.0.0-preview.1, the current behaviour is safe. For v1.1.0, either:
- **Option A:** Use a sensible non-1 default (e.g., `PageSize: 10`) when `totalCount == 0`
- **Option B:** Add a `<remarks>` note to `ListPagedAsync` documenting the fallback
- **Option C:** Introduce a `PagedResult.Empty<T>()` factory that communicates intent explicitly

Option B is the lowest risk. Option C is the cleanest API if the concept of "unpaginated paged result"
is worth making explicit. Decision to be made before v1.0.0-final.
