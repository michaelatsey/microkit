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
   `MicroKit.Persistence.Abstractions` depends on `MicroKit.Domain` (for
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

### Amendment — #196 D1, D6 (2026-11-08)

- **D1.** `QueryOptions<T>.IncludeDeleted` (and `IncludeSoftDeleted()`) is removed, together with the
  evaluator's "step 0", which called the parameterless `IgnoreQueryFilters()`. That call disabled
  **every** query filter of the model — a tenant filter included — while the module owns no soft
  delete. The original order "IgnoreQueryFilters → criteria → …" is superseded by the five steps above.
  `IlCallScanTests` fails on any library call to the parameterless overload; #197 reintroduces the
  flag on a *named* filter (`IgnoreQueryFilters(["<name>"])`).
- **Wording.** Since #169, `Specification<T>` in `MicroKit.Domain` exposes `ToExpression()` (and
  `IsSatisfiedBy`), not `AddCriteria` / `Criteria`: read the rationale and consequences above with
  that API. The decision itself — criteria only, no includes or paging in a specification — stands.
- **D6.** Pagination is validated (`PaginationOptions(page >= 1, pageSize >= 1)`, offset within
  `int`) and bounded: `PaginationLimits` (default page 50, maximum 500, configured by
  `ConfigurePagination`) makes `ListPagedAsync` without pagination return the default page instead
  of the whole result set, and rejects — never clamps — a larger page. This supersedes DN-002.

### Amendment — #197 phase 1 (2026-11-09)

Tracking leaves `QueryOptions<T>`: `AsNoTrackingEnabled`, `WithTracking()` and `AsNoTracking()` are
removed, and `QueryOptions` is the loading strategy of the read side only (includes, split query,
ordering, pagination). The read side never tracks (ADR-009); read "whether to track" out of the
rationale above.

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
- The `dependency-guardian` agent checks this on every `.csproj` change.

### Amendment — #197 phase 4 (2026-11-09)

The evaluation order gains a step 0: `QueryOptions.IncludeDeleted` ignores the soft-delete filter only
(`IgnoreQueryFilters([SoftDelete.FilterName])`), before the criteria — ADR-012 §8.

### Amendment — #196 D8, D9, D10, D12, D17 (2026-11-08)

The inventory in the Decision above is superseded. Abstractions now holds exactly:

- `IRepository<TAggregate, TId>` — the only write-side contract (D10); no `CommitAsync` (D9);
- `IUnitOfWork`, `ITransactionalContext`, `IPagedResult<T>`, `PersistenceException`.

Removed: the empty `IReadRepository<T>` marker (D8 — the read contract needs `QueryOptions`, so it
lives in Core, the only `IReadRepository<T>`), and `ITransaction` / `ITransactionManager` (D12 —
implemented and used nowhere). The package references `MicroKit.Domain` only: the `MicroKit.Result`
reference is removed (D17), because nothing in Abstractions used it; the earlier claim that
repository methods "return `Result<T>`" never matched the code.

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

- DI registration maps one scoped `EfUnitOfWork<TContext>` to `IUnitOfWork`, `ITransactionalContext`
  and `ITransactionalUnitOfWork` (`AddUnitOfWork<TContext>`).
- `ITransactionalUnitOfWork` is not part of the published API in Abstractions — it is an
  implementation detail of the EF Core integration.
- If a future `Marten` or `Dapper` provider is added, it registers its own scoped service implementing
  `IUnitOfWork` + `ITransactionalContext` without needing a composite interface.

---

## ADR-005: IUnitOfWork Gains DiscardChanges() — Completing the Unit of Work Contract

**Status:** Accepted
**Date:** 2026-08-20
**Related:** ADR-001 (IUnitOfWork placement), ADR-003 (Abstractions minimality), ADR-004 (EF composite),
DN-001 (execution-strategy gap), ADR-MEDIATR-011 (TransactionBehavior → Persistence.Abstractions)

### Decision

`IUnitOfWork` gains a second member in `MicroKit.Persistence.Abstractions`:

```csharp
public interface IUnitOfWork
{
    ValueTask CommitAsync(CancellationToken ct = default);
    void DiscardChanges();
}
```

`DiscardChanges()` abandons the pending change set without writing it. The EF Core implementation is
`ChangeTracker.Clear()`. Every command boundary that does **not** commit — business failure *and*
thrown exception — must call it before the boundary ends. The caller is
`MicroKit.MediatR.Behaviors.TransactionBehavior`, never the command handler.

### Rationale

**1. The Unit of Work contract was incomplete, and the gap is a data-corruption bug.**

`TransactionBehavior` returns early on `Result.IsFailure`: it dispatches nothing and flushes nothing.
The database transaction then commits clean — no exception was thrown, so
`EfUnitOfWork.ExecuteAsync` reaches `transaction.CommitAsync`. But the entities the handler staged
are still in the change tracker as `Added`/`Modified`, and `DbContext` is **scoped**, not
per-command. The next `SaveChangesAsync` in that scope — from any later command — writes everything
the tracker holds, including the failed command's entities.

The result is the worst failure class available: a command that explicitly failed its business rule
persists its data anyway, with no error, no log, and no signal distinguishing it from correct
operation.

**2. The contract is stated over every non-commit exit, not over `Result.IsFailure`.**

The exception path has the identical defect. `ExecuteAsync` rolls back the database transaction and
rethrows; EF Core's rollback does **not** reset the change tracker. Staged entities survive a
rollback exactly as they survive a business failure. Any fix scoped to `IsFailure` leaves the hole
open for every thrown `PersistenceException`, `DbUpdateConcurrencyException`, and handler
exception — which are, for the persistence layer specifically, the *more* common failure mode. A
fix scoped to `IsFailure` would be half a fix.

**3. Reachability.**

Inert under strict one-scope-per-command hosting — the common ASP.NET Core request path. Reachable
wherever a scope outlives a single command:

- consumer-authored batch loops that create one scope per batch rather than per message
- an `INotificationHandler` that sends two commands through `IMediator` within one message scope
- scheduled jobs
- integration tests that chain commands in one scope
- long-lived scopes — a Blazor Server circuit's scope lives for the whole session

MicroKit's own `OutboxProcessor` and `InboxProcessor` are **not** examples: both create a scope per
message (`OutboxProcessor.cs:79`, `InboxProcessor.cs:93`), complying with the monorepo's
`IAsyncServiceScope`-per-message rule. That rule exists because scope-per-batch is the common
mistake, and it cannot be enforced in consumer code.

**4. This is a latent property surfaced, not a regression.**

Before `fix(mediatr-behaviors)` (#78, `4380b4e`) nothing on the command path ever called
`SaveChangesAsync`, so a dirty tracker had no way to reach the database. Repairing the silent
no-write made the residue reachable. The defect is older than the fix that exposed it.

**5. Fowler's Unit of Work has two exits; ours had one.**

A unit of work accumulates a change set and then either commits it or abandons it. `IUnitOfWork` as
shipped can only commit. Every consumer that needs the other exit must reach past the abstraction to
the provider — which for an EF-free consumer like `MicroKit.MediatR.Behaviors` means referencing
`MicroKit.Persistence.EntityFrameworkCore` and calling `ChangeTracker.Clear()` directly, exactly the
ADR-004 violation this module forbids. The member is not a convenience; it is the missing half of
the pattern the interface is named after.

**6. It passes the ADR-003 minimality test — and is load-bearing for a non-EF consumer.**

> "Could a consuming module that does NOT use EF Core reference this package and compile?"

`void DiscardChanges()` names no EF type, no `DbContext`, no `ChangeTracker`. A Dapper, Marten, or
in-memory implementer compiles against it unchanged. More than passing the test, the member exists
*because* of it: `TransactionBehavior` has no EF Core reference and must remain that way
(ADR-MEDIATR-011), so the only provider-agnostic way for it to abandon a change set is a member on
the Abstractions contract.

The concept is genuinely cross-ORM, not an EF idiom in disguise — EF Core `ChangeTracker.Clear()`,
Marten `IDocumentSession.EjectAllPendingChanges()`, NHibernate `ISession.Clear()`. A provider with no
change set at all (Dapper, raw SQL) satisfies it as a no-op, and satisfies `CommitAsync` just as
trivially; "commit or discard the pending change set" holds vacuously where there is no pending
change set.

**7. `void`, not `ValueTask` — because sync is the reversible direction.**

No plausible implementation performs I/O: the operation drops in-memory references. EF Core's
`ChangeTracker.Clear()`, Marten's `EjectAllPendingChanges()`, and NHibernate's `Clear()` are all
synchronous with no async counterpart. `rules/performance.md` mandates `ValueTask` for repository
methods on the grounds that "a synchronous ... path allocates no state-machine box" — that rationale
argues *against* wrapping a purely synchronous operation, not for it.

Two further reasons decide it:

- **An async signature would misdescribe the contract.** It tells callers "this may await" when no
  implementation can, and invites `await` inside a `catch`/`finally` where a synchronous call is
  strictly safer.
- **Sync is the reversible choice.** If a future provider ever needs to release a remote session
  asynchronously, adding `DiscardChangesAsync` alongside is *additive*. Removing an unnecessary
  `ValueTask` later is *breaking*. Start at the signature that can be widened.

**8. Naming: `DiscardChanges`, and emphatically not `Rollback`.**

This module has just spent a release cycle on damage from one homonym: `IUnitOfWork.CommitAsync`
(the flush) versus `IDbContextTransaction.CommitAsync` (the SQL commit). That collision is precisely
what let a behavior "commit" while writing nothing, in code and in its own XML documentation.
Naming this member `Rollback()` would create the second homonym pair in the same contract —
`IUnitOfWork.Rollback` (in-memory, no I/O) against transaction rollback (database, real) — and
guarantee a repeat of the same confusion. `Clear()` is EF-flavoured and vague on a public contract.

`DiscardChanges` states the effect, names no mechanism, and collides with no transaction verb. The
absent `Async` suffix carries real information: this touches nothing outside the process.

**9. The behavior owns the call, not the handler.**

A handler cannot know whether its scope holds one command or twenty; the behavior is the only place
that knows a command boundary just ended. And the behavior already owns the *commit* decision — it
decides to flush on success. Splitting ownership of a boundary between behavior and handler is how
the original defect arose. Both exits belong to the same owner.

This also needs no new dependency: #78 already put `IUnitOfWork` in the behavior's constructor.

### Alternatives Rejected

| # | Alternative | Why it loses |
|---|---|---|
| **A** | Document a one-command-per-scope constraint; no code | Unenforceable — no analyzer can see a consumer's scope shape. Worse, it is **false**: Blazor Server circuits, long-lived worker scopes, and integration tests all legitimately run many commands per scope, so the constraint forbids supported topologies instead of describing them. Correctness would depend on a document the consumer may never read — the same failure mode as the XML doc that asserted `TransactionBehavior` flushed. |
| **C** | Do nothing; document the risk | Strictly worse than A: same unenforceability, plus it books silent data corruption as an accepted property. Silent + wrong + indistinguishable-from-correct is not a documentable risk class. |
| **D** | Selective discard — detach only the entries this command staged | Requires snapshotting the tracker *before* every handler, including the successful majority: an always-paid O(tracked entities) cost to improve a rarely-taken path. EF Core offers no "revert the tracker to a prior state" primitive, so the snapshot and diff would be hand-rolled. Complexity and constant cost exceed the benefit over a full clear. |
| **E** | Roll back the transaction on business failure instead of committing | Does not fix it. EF Core's transaction rollback leaves the change tracker untouched — entities stay `Added`/`Modified` through a rollback exactly as through a commit. This is the most natural objection and it fails on a fact: it addresses the wrong layer. |
| **F** | Database savepoints | Same error: nothing was written. The residue is in memory, and no database-side construct reaches it. |
| **G** | Have `TransactionBehavior` open a child DI scope per command | Structurally sound, far more invasive: it changes lifetime semantics for every scoped service a handler touches, and the behavior cannot know which scoped services the host intends to share across a scope. One interface member against a redefinition of the DI contract. |
| **H** | Put the member on `ITransactionalContext` instead | `ITransactionalContext` owns a *transaction*; the pending change set is not its property. A discard member there would imply transactional meaning it does not have (the operation is purely in-memory) and would force change-set semantics onto every implementer, including those that manage transactions without a unit of work. The composite `ITransactionalUnitOfWork` is unavailable by ADR-004 — it lives in `EntityFrameworkCore`, and `TransactionBehavior` must stay EF-free. |

(Option **B** — `DiscardChanges()` on `IUnitOfWork` — is the decision recorded above.)

### Consequences

**Breaking change to a published STABLE contract.** Every `IUnitOfWork` implementer must add the
member. In-repo there are exactly two; external implementers get the migration snippet below.

**`DiscardChanges()` clears the whole scope's tracker, not one command's entities.** In a scope where
command A succeeded (and therefore flushed) and command B failed, B's discard also detaches A's
entities. A's work is already durable, so correctness holds — but every entity reference the caller
still holds becomes detached. The in-memory object graph is left intact (EF Core's
`ChangeTracker.Clear()` resets the state manager rather than detaching entry by entity, so existing
navigation references survive), but EF no longer tracks it, so re-saving such a reference later
inserts a duplicate. Post-failure the pipeline is unwinding, so nothing downstream should depend on
tracked state. Pathological, and recorded here rather than left to be discovered.

Detachment is quieter than it sounds, which is the part worth stating explicitly: **lazy and explicit
navigation loads on a detached entity do not throw** — verified on EF Core 10.0.9 for both the
`ILazyLoader` form and `Entry(e).Collection(...).Load()`. They issue a fresh query and return the
data, without re-tracking the entity. Inside a failing command that means a round-trip on a
transaction that is about to roll back, with no signal to the caller that anything is wrong. A prior
revision of this ADR asserted the opposite ("navigation fixup stops and lazy loading throws"); it was
wrong on both halves, and the correction is recorded here because the claim had already been copied
into the `IUnitOfWork.DiscardChanges` XML documentation.

> **Superseded in part by #196 D4.** The paragraph below says nested dispatch "is already blocked
> upstream — EF Core rejects `BeginTransactionAsync` while a transaction is open". Since #196 D4,
> `ExecuteAsync` joins an open transaction instead of beginning one, so nothing blocks a nested
> command any more, and its `DiscardChanges` on an inner failure still clears the outer command's
> staged work. The consequence is recorded in ADR-006; the original reasoning is kept below.

**Nested command dispatch stays unsupported, now for a second reason.** A command dispatched from
inside another command's handler would, on inner failure, clear the outer command's staged work. This
is already blocked upstream — EF Core rejects `BeginTransactionAsync` while a transaction is open on
the connection — so no new breakage is introduced, but the prohibition is now load-bearing in two
places and should be stated in the `TransactionBehavior` documentation rather than left to EF Core to
enforce at runtime.

**Secondary cost removed.** Surviving tracked entities enlarge every
`ChangeTracker.Entries<IHasDomainEvents>()` enumeration for the remaining life of the scope, and
`EfDomainEventsProvider.DrainDomainEvents()` triggers `DetectChanges` on each drain — O(tracked
entities) per subsequent command. Discarding bounds the tracker to the current command's working set.

**Compatible with the deferred retry work (DN-001).** Discarding on a failed attempt is the correct
semantic for a retrying execution strategy — each attempt should begin from a clean tracker. It does
not fix retry (which remains broken independently: `DrainDomainEvents()` is destructive, so a second
attempt finds no events) and does not obstruct the eventual fix.

**Placement of the call is a `MicroKit.MediatR` decision, not this one.** Recommended: inside the
`ExecuteAsync` lambda rather than around it, so the discard lands on the retry-attempt boundary. This
ADR fixes the contract and the requirement — *every* non-commit exit discards — and leaves the call
site to the consuming module's own ADR.

### Implementation — every consumer that must change

| # | Project | Change | Kind |
|---|---------|--------|------|
| 1 | `MicroKit.Persistence.Abstractions` | `IUnitOfWork` gains `void DiscardChanges()` | **Breaking** |
| 2 | `MicroKit.Persistence.EntityFrameworkCore` | `EfUnitOfWork<TContext>.DiscardChanges()` → `context.ChangeTracker.Clear()` | Required |
| 3 | `MicroKit.Persistence.Testing` | `InMemoryUnitOfWork.DiscardChanges()` + `DiscardCount` for assertions, symmetric with `CommitCount` | Required |
| 4 | `MicroKit.MediatR.Behaviors` | `TransactionBehavior` calls it on business failure **and** on exception. No constructor change — `IUnitOfWork` was already injected by #78 | Required, separate PR |

**Not affected:** `MicroKit.Persistence.Analyzers.Tests` — its `IUnitOfWork` is a self-contained
source stub in a raw string literal, compiled independently of the real assembly. `MKP001`–`MKP005`
match on the full type name and need no new rule: `IReadRepository` implementers never implement
`IUnitOfWork`. `MicroKit.Domain.Repositories.IUnitOfWork` is a **different, unrelated interface**
(`Commit`/`Rollback`/`Repository<T>`, `: IDisposable`) that survived the ADR-001 move; it does not
implement this contract and is untouched.

### Migration

```csharp
// Any external IUnitOfWork implementation must add the member.
public sealed class MyUnitOfWork(MyContext context) : IUnitOfWork
{
    public ValueTask CommitAsync(CancellationToken ct = default) => /* unchanged */;

    // EF Core:
    public void DiscardChanges() => context.ChangeTracker.Clear();

    // A provider with no pending change set (Dapper, raw SQL) — writes are already durable:
    // public void DiscardChanges() { }
}
```

Consumers who only *inject* `IUnitOfWork` are unaffected. No call site changes; the member is called
by `TransactionBehavior`, not by handlers.

### Version and CHANGELOG implications

`.claude/workflows/releasing-module.md` § **Breaking Change Protocol** names `IUnitOfWork` signature
changes explicitly and requires:

1. `feat(persistence)!:` on the commit scope
2. `BREAKING CHANGE:` footer carrying the migration snippet
3. Version increment
4. This ADR (satisfied)

**On the version.** The protocol says "increment major version". It was written for a post-1.0
module; MicroKit.Persistence is at `1.0.0-preview.3`. Applying it literally yields `2.0.0` for a
package line that has never shipped a stable release — meaningless to consumers and disruptive to the
shared-version scheme. The preview line is the natural instrument for a break of this kind.

**The member must land before `1.0.0` final.** Once a stable release ships, adding it requires a
genuine major version bump. That is the deadline this decision runs against.

The version number itself is the release owner's call; this ADR records the implication, not the
choice. All 8 packages share one version — whichever number is chosen, all 8 republish, including the
6 untouched.

---

## ADR-006: Transaction Semantics of EfUnitOfWork — Retry, Join, Rollback (#196 D3–D5)

**Status:** Accepted
**Date:** 2026-11-08
**Source:** Issue #196, phase 1 (D3, D4, D5). Supersedes DN-001.

### Decision

`EfUnitOfWork<TContext>` keeps its public surface (`IUnitOfWork`, `ITransactionalContext` and
`ITransactionalUnitOfWork` are frozen) and changes behaviour as follows.

1. **Translation (D3).** `CommitAsync` always translates to `PersistenceException`: a
   `DbUpdateException`, and the `InvalidOperationException(DbUpdateException)` that a provider's
   non-retrying strategy (Npgsql, SQL Server) raises inside `SaveChangesAsync` for a failure it
   classifies as transient.
2. **Retry (D3).** Inside the execution-strategy delegate, `ExecuteAsync` unwraps a
   `PersistenceException` whose inner exception is a `DbUpdateException` and rethrows that inner
   exception, so the strategy can classify it — `ExecutionStrategy.CallOnWrappedException` only
   looks through exceptions EF Core created. Once the strategy gives up, the outermost call surfaces
   a `PersistenceException` only:
   - the original one, for a non-transient commit failure;
   - a new one wrapping `RetryLimitExceededException`, when retries are exhausted;
   - a new one wrapping the `DbException`, for a failure of the transaction itself (BEGIN, COMMIT,
     a deferred constraint) or of raw SQL the operation ran, or the provider's
     `InvalidOperationException` wrapper of a transient one;
   - a new one wrapping the `DbUpdateException` (or the provider's `InvalidOperationException`
     wrapper of it), when the operation flushed through the `DbContext` itself rather than
     `CommitAsync`.
3. **Join (D4).** While `Database.CurrentTransaction` is not null — an enclosing `ExecuteAsync`, or
   a transaction the caller opened — `ExecuteAsync` runs the operation directly: no execution
   strategy (a retrying one throws on an existing transaction, `ExecutionStrategy.OnFirstExecution`),
   no begin, commit or rollback, no translation. The transaction's owner commits, rolls back,
   retries and translates. An ambient `System.Transactions` transaction is rejected, as EF Core
   rejects it (#197 phase 3, pinned by tests): with a retrying strategy, `OnFirstExecution` raises
   `ExecutionStrategyExistingTransaction`; without one, `BeginTransactionAsync` raises
   `ConflictingAmbientTransaction`. A distributed transaction is not a consistency mechanism between
   services: they go through the outbox.
4. **Failed attempt (D5).** The rollback runs with `CancellationToken.None`; rollback and dispose
   failures are swallowed, so the exception in flight is never replaced. The change tracker is then
   cleared: the database was rolled back, so a retried attempt must start from a clean tracker
   (otherwise a change `SaveChanges` already accepted before a failed COMMIT is applied twice).

### Rationale

- **The presumption was proven** (`EfUnitOfWorkRetryTests`, retrying strategy, PostgreSQL): before
  the change, a transient `SaveChangesAsync` failure inside `ExecuteAsync` ran once and surfaced
  `PersistenceException → DbUpdateException → transient cause`; it was never retried.
- **Unwrap inside, not "don't translate while running"**: the issue first proposed that `CommitAsync`
  stop translating while an `ExecuteAsync` runs. That would hand a handler that catches
  `PersistenceException` a raw `DbUpdateException`, breaking `IUnitOfWork.CommitAsync`'s documented
  contract. Unwrapping in `ExecuteAsync` gets the retry without touching `CommitAsync`'s contract.
- **The original instance is surfaced** for a non-transient failure, so a handler that recorded it
  (for logging, for mapping) sees the same exception the caller sees.
- **Joining** gives nested `ExecuteAsync` calls and caller-owned transactions a defined behaviour
  instead of the `InvalidOperationException` EF Core raised from `BeginTransactionAsync`
  ("The connection is already in a transaction").

### Consequences

- **At-least-once.** With a retrying strategy the whole operation re-runs after a transient
  failure. A failure of the COMMIT itself is ambiguous (the database may have committed);
  `verifySucceeded` is not used, so a non-idempotent operation (store-generated keys, external side
  effects) can apply twice. Keep retries off for those, or make them idempotent.
- **Nested command dispatch is no longer blocked (finding for `MicroKit.MediatR`).**
  `TransactionBehavior.cs:65-68` states that nested dispatch is unsupported and that EF Core rejects
  it upstream because `BeginTransactionAsync` throws while a transaction is open. After D4 nothing
  rejects it: the inner command's `ExecuteAsync` joins the outer transaction. On an inner business
  failure (`TransactionBehavior.cs:129`) or exception (`:149`), the inner behaviour calls
  `DiscardChanges()`, which clears the whole scoped change tracker — including the outer command's
  staged aggregates and their events. If the outer handler continues and returns success, its
  commit writes only what was staged after the discard, and the transaction commits: the outer
  command's earlier work is lost without an error. A pipeline that discards per command must
  detect the nesting (an open transaction) and either reject it or discard only at the outermost
  level. This module leaves `TransactionBehavior` untouched (#196 perimeter).
- **Tests.** `EfUnitOfWorkRetryTests`, `EfUnitOfWorkJoinTests` (each case under the default and a
  retrying strategy), `EfUnitOfWorkRollbackTests` — all against PostgreSQL.

---

## ADR-007: Whole-Aggregate Typed Load, No Update, No Repository Commit (#196 D9–D11)

**Status:** Accepted
**Date:** 2026-11-08
**Source:** Issue #196, phase 2.

### Decision

1. **One write-side contract.** `IRepository<TAggregate, TId>` (`TAggregate : class, IAggregateRoot`,
   `TId : IEntityId`) replaces `IRepository<TAggregate>`: `FindAsync(TId)`, `AddAsync`, `DeleteAsync`.
2. **No commit on the repository (D9).** Committing belongs to `IUnitOfWork` only; the repository's
   `CommitAsync` let a handler commit outside `TransactionBehavior`, which MKP003 exists to prevent.
3. **No update (D11).** A loaded aggregate is tracked, so its changes need no call; `DbSet.Update`
   marked a whole graph `Modified` — redundant when tracked, unsafe when detached.
4. **Typed, whole load (D10).** `EfRepository<TAggregate, TId, TContext>.FindAsync(id)`:
   - returns an aggregate the unit of work already tracks as is, without a query;
   - otherwise builds the id predicate from the EF model's single-property primary key (whose CLR type
     must be `TId`; no `AggregateRoot<TId>` base class is required) and loads, tracked, every
     navigation — reference, collection, skip; derived types included — whose target is not an
     `IAggregateRoot`, recursively, a cycle being cut against the current path's ancestors only;
   - splits the load when the aggregate has two or more collections (owned collections mapped to a
     table included); otherwise leaves the context's split setting alone.
   The protected virtual `IncludeAggregate` is the seam #197's per-aggregate load profiles replace.

### Consequences

- An entity not marked `IAggregateRoot` is inside the boundary — reference data included — and a
  self-referencing child is followed one level only; `IncludeAggregate` overrides both.
- A split load is not snapshot-consistent under Read Committed (documented on `IncludeAggregate`).
- `InMemoryRepository<TAggregate, TId>` implements both repository contracts on one store, and fails
  fast where EF Core fails (an `Id` of another type, a duplicate `AddAsync`).
- Tests: `EfRepositoryAggregateLoadTests` (SQLite and PostgreSQL).

### Amendment — #197 phase 1 (2026-11-09)

`IncludeAggregate` is no longer `protected virtual`: it is private, and the boundary is read from the
model only. Reference data leaves the boundary through `IsOutsideAggregate()` on its navigation, not
through an override; a typed finder loads through `protected LoadAggregatesAsync(predicate)`.
`FindAsync` returns a tracked root as is only when the repository loaded it whole or it is `Added`
(ADR-009). The consequence "`IncludeAggregate` overrides both" no longer holds: a self-referencing
child is still followed one level only, and there is no per-repository override.

---

## ADR-008: MicroKit.Domain Mapping Conventions (#196 D13, D14)

**Status:** Accepted
**Date:** 2026-11-08
**Source:** Issue #196, phase 3. Resolves #172.

### Decision

`DomainConventionExtensions` (for `ConfigureConventions`) ships the mappings every consumer otherwise
writes by hand:

- `AddEntityIdConventions(assemblies)` — every `record X : EntityId<X>` of the given assemblies, and
  `GuidId`, to a `Guid` column (pre-convention configuration). Values are read back through the
  type's `(Guid)` constructor whatever its accessibility (`EntityId<T>`'s own constructor is
  protected and `MicroKit.Domain` is not modified); model building fails, naming the type, when
  there is none. Converted keys stay `ValueGenerated.Never` (EF Core's default for them).
- `AddActorConvention()` — `Actor` to a string of at most `Actor.MaxLength`, read back through
  `Actor.Restore`, so the built-in actors keep their shared instances.
- `AddAuditInstantConvention()` (D14) — an interactive property-added convention that normalises
  `IAuditableEntity.CreatedAt` / `UpdatedAt` to offset zero on read and write, and rejects a stored
  `default` instant on read. Writes and query parameters are only normalised: rejecting `default`
  there would fail `CreatedAt >= DateTimeOffset.MinValue`.

### Rationale

Materialisation writes the audit backing fields directly, bypassing the `Auditable*` constructors:
a row stored with a non-zero offset (SQL Server `datetimeoffset`, SQLite text) came back with it,
and a later write to a PostgreSQL `timestamptz` column failed; a `0001-01-01` row loaded silently.
Scope is limited to audit instants: other `DateTimeOffset` properties keep their offset by design.

### Consequences

- A conversion configured explicitly — or a pre-convention `DateTimeOffset` converter — takes
  precedence and turns the normalisation off for those properties.
- Compiled models and NativeAOT precompiled queries are not supported: a `dotnet ef dbcontext
  optimize` spike showed the generated model instantiating the internal converters and calling
  internal `UtcInstant` members, so it does not compile. Making the converters public is additive and
  can be done later.
- `IEntityId` record structs other than `GuidId` still need a hand-written converter.
- Tests: `DomainConventionTests`, `DomainConventionModelTests`, `AuditInstantNormalisationTests`.

---

## ADR-009: The Aggregate Boundary Lives in the Model; Only Whole Roots Are Tracked (#197 phase 1)

**Status:** Accepted
**Date:** 2026-11-09
**Source:** Issue #197, phase 1. Amends ADR-002 and ADR-007; reverses #196 D2.

### Decision

1. **Activation.** `DbContextOptionsBuilder.UseMicroKitPersistence()` adds an internal
   `IDbContextOptionsExtension` that registers MicroKit's services in the context's internal service
   provider: a convention-set plugin (an EF Core `IConventionSetPlugin`, through `TryAdd`) and a
   scoped `AggregateLoadRecord` (through `TryAddProviderSpecificServices`, as it is not an EF Core
   service), also registered as EF Core's `IResettableService` (through `TryAdd`) so a pooled context
   resets it. `AddDbContext`, `UsePostgreSQL` and `UseSqlServer` call it. Repository constructors are
   unchanged. The extension has no options: its `ExtensionInfo` returns hash 0 and shares the service
   provider with any other instance of itself; an option added later must take part in both.
2. **The boundary lives in the model, and only there.** Default: #196 D10's derivation (every
   navigation to a non-`IAggregateRoot` type, recursively). `IsOutsideAggregate()` on a
   `NavigationBuilder` sets the annotation `MicroKit:OutsideAggregate` (`bool`, code-generatable) and
   excludes the navigation. `IncludeAggregate` is private: no repository redefines the boundary.
3. **A child has one root.** A model-finalizing convention walks every top-most root's boundary and
   fails model building, naming the type and the roots, when a non-root, non-owned entity type — or a
   derived type of one — is reached from two roots. Each ownership of an owned type is its own entity
   type and is never shared. The convention writes no annotation; the include plan stays computed from
   the runtime model and cached per `IEntityType`.
   **Uniqueness also holds per row** (Spec amended 2026-11-09T14:50Z): a navigation brings a non-root
   entity into the boundary only if each of its rows can belong to one parent row — a collection or a
   reference whose child holds the foreign key, or a reference whose parent holds a **unique** foreign
   key (one-to-one). A many-to-one reference held by the parent that reaches a non-root entity fails
   model building, naming the navigation and the fix: make the target an aggregate root, or mark the
   navigation `IsOutsideAggregate()`. A skip navigation (many-to-many) declared by a type inside a
   boundary fails model building **whatever its target**, root or not, unless it is marked
   `IsOutsideAggregate()` (on either side: a unidirectional one has a hidden inverse). The fix it names is
   an explicit link entity, a child of the root (`HasMany(o => o.TagLinks)`, the foreign key on the
   link, the link referencing the other root): the links are loaded, versioned and removable like any
   child, without tracking the other root, which loads whole through its own repository. A skip
   navigation marked outside is neither loaded nor versioned; its join rows lie outside every
   boundary.
4. **Only whole roots are tracked through the write side.** The record holds, by reference, the roots
   `EfRepository` loaded with the whole plan:
   - a root the repository's load **returned**, once that load has **completed**, and only if it was
     tracked `FromQuery` during that load, enters it — a load that fails midway records nothing, and a
     root the model auto-includes without its own aggregate (same hierarchy included) is never
     recorded;
   - a root staged by `EfRepository.AddAsync` enters it: the application built it whole, and it stays
     returnable once the commit makes it `Unchanged`. A root added directly on the context
     (`DbContext.Add`, `DbSet.Add`) is not recorded: it is returned while `Added`, refused once
     committed. The Spec's first text admitted only roots tracked `FromQuery` during the repository's
     own load, which refused an aggregate added, committed and found again in the same context (code
     review of phase 1); that deviation is closed by the 2026-11-09 amendment;
   - any other `ChangeTracker.Tracked` event of a root, and a change to `Detached`, removes it;
   - `DiscardChanges` and a failed `ExecuteAsync` attempt reset it (`ChangeTracker.Clear` raises no
     event), and so does the pool. Roots are held weakly (`ConditionalWeakTable`, by reference): after
     a `Clear()` the application calls itself, nothing recorded keeps a graph alive;
   - every load subscribes again, idempotently, because returning a context to its pool drops every
     change-tracker handler.
   `FindAsync` and `LoadAggregatesAsync` refuse, with an `InvalidOperationException` naming the root
   type, a tracked root that is neither recorded nor `Added`. `LoadAggregatesAsync` checks the roots
   **after** its query: identity resolution returns an already-tracked partial root without a
   `Tracked` event. `IsLoaded` is not used: a filtered `Include` marks a navigation loaded.
5. **The read side never tracks** and never consults the change tracker. `EfReadRepository`'s five
   methods run `AsNoTracking()` — re-applied after the evaluator, so a delegate cannot opt back in —
   and return new instances. This reverses #196 D2 ("a tracked instance is returned as is"). It also
   departs from the Spec's first text, which had `EfReadRepository.FindAsync` and `ListAsync` refuse
   an unrecorded tracked root; the Spec was amended on 2026-11-09 to match: a read that never returns
   a tracked instance cannot hand out a partial one, so a refusal there would only make reads fail.
   The refusal applies to the write side only, identity resolution included.
6. **An owned navigation is never outside the aggregate.** `IsOutsideAggregate()` on an ownership
   navigation fails model building: EF Core loads an owned entity with its owner anyway, and skipping
   it would only drop its own navigations from the load and from the one-root check.

### Rationale

A partial aggregate reaching a command breaks every later guarantee built on the boundary
(concurrency per aggregate, soft delete of the whole boundary — #197 phases 2 and 4). A typed finder
taking a predicate, not a query, cannot narrow the load. The read side returning the tracked
instance was a way for a query to observe — and for a command to depend on — a partial aggregate;
with the read side untracked, only the write side can hand out a tracked root, and it hands out
whole ones only.

### Consequences

- A context not configured with `UseMicroKitPersistence()` cannot use `EfRepository`; the error says
  so. `EfReadRepository` and `EfUnitOfWork` work without it.
- Known limit: between a pool reset and the next load, an attach is not observed; the record is
  empty then, so nothing recorded can be attached unseen.
- `ITransactionalUnitOfWork`, `EfUnitOfWork`'s public surface, `IUnitOfWork`, `ITransactionalContext`,
  `PersistenceException`, `AddMicroKitPersistence` and `AddUnitOfWork` are unchanged.
- Tests: `AggregateBoundaryTests` (PostgreSQL and SQL Server), `EfReadRepositoryFindTests`,
  `EfReadRepositoryTrackingTests`, `EfRepositoryAggregateLoadTests`.

---

## ADR-010: Application-Managed Aggregate Version (#197 phase 2)

**Status:** Accepted
**Date:** 2026-11-09
**Source:** Issue #197, phase 2. Builds on ADR-009 (one root per child, the loaded-root record).

### Decision

1. **The token.** A model-finalizing convention, first among the finalizing conventions (EF Core's
   table-sharing concurrency convention must see it), adds a shadow `long` property
   `AggregateVersion` with `IsConcurrencyToken()` to every top-most aggregate root. Only the name is
   reserved: no column name is set, so a naming convention or the application's `HasColumnName` /
   `HasColumnType` applies. Model building fails, naming the root, when the property is mapped to a
   CLR member, is not `long`, is no longer a concurrency token, is given a default value (or SQL), a
   computed column, value generation, a value converter or a provider type (the save step alone writes
   it; added in the #197 phase 4 review, as for the soft-delete mark), or shares its column with another
   property of a table that stores it — the root's, or each concrete type's under TPC, whose abstract base
   has no table; only the entity types mapped to that table count, and two roots under one mapped TPH base
   that is no root share their version column. No change to `MicroKit.Domain`.
2. **The bump.** A `SaveChanges` step sets the token of every root whose boundary has an `Added`,
   `Modified` or `Deleted` entity, the root included: 1 on insertion; original + 1 otherwise,
   computed from the original value so a repeated save never adds twice; a `Deleted` root keeps its
   original, which its `DELETE` checks. A root whose only change is a child is written for its token
   alone (`UPDATE … SET AggregateVersion WHERE AggregateVersion = @original`), so a change confined to
   a child conflicts — which a native `xmin`/`rowversion` would not.
3. **Attaching a child to its root** goes through the boundary of ADR-009 (`AggregateBoundaryMap`,
   built from the phase-1 walk): a dependent child by its original foreign key when deleted or moving
   out, by its current one otherwise; a child the parent references (the parent holds the FK) by the
   parents' original **or** current foreign key, so a child replaced or deleted in the same save still
   finds its root; a skip navigation through its join entity. Lookups use the public change-tracker
   API only (an index over `ChangeTracker.Entries()` per save).
4. **Refusals.** A child moving between two aggregates bumps both and requires both to be recorded
   (ADR-009); otherwise the save fails naming the roots. A changed child inside a boundary whose root
   is not tracked fails the save — including a move to or from an aggregate whose root is not
   tracked (a foreign key pointing to an untracked parent leaves that side unresolved). An entity outside every boundary (outbox and inbox rows, reference
   data, an `IsOutsideAggregate()` target no root reaches otherwise) is ignored.
5. **Across requests.** `IRepository.GetVersion(aggregate)` reads the token; `FindAsync(id,
   expectedVersion)` loads as `FindAsync(id)` and makes `expectedVersion` the token's original value,
   so the check runs in the commit's `UPDATE`, never as a read-then-compare. A command stating the
   current version and changing nothing writes nothing; a stale version conflicts even then (EF Core
   sees original ≠ loaded and writes the token). `AggregateVersion.PropertyName` serves projections.
   The in-memory double checks the expected version at the find. A staged root not inserted yet has
   version 0 and its `INSERT` checks nothing, so for it the expectation is checked at the find.
   A save that fails or is canceled undoes its bumps (`SaveChangesFailed`, `SaveChangesCanceled`), so
   `GetVersion` never reports a version that was not committed.
7. **The version API is on `IRepository` (Abstractions).** Chosen over an EF-only
   `EfRepository.GetVersion`/`ExpectVersion` and over a separate `IAggregateVersions` service: a
   command handler injects only its repository, typed or not, and the ETag flow needs no EF Core
   type. Cost: every implementation of the contract must support versions; the in-memory double
   cannot see mutations, so it never bumps on its own (`SetVersion` for tests) and checks the
   expectation at the find, which the contract allows.
6. **One save pipeline.** `AggregateSaveInterceptor` (registered by `UseMicroKitPersistence()` as an
   EF Core `IInterceptor`) is MicroKit's single `SaveChanges` entry point. It detects changes first —
   `SavingChanges` runs before EF Core's own `DetectChanges` (step-0 spike) — then runs its steps in
   a fixed order in code, never left to EF Core's interceptor order. The version step is always last:
   #197 phase 4 inserts its soft-delete conversion (a `Deleted` root becomes `Modified`) in front of it,
   so a soft-deleted root is bumped like any modified one — done: `AggregateSoftDeleteStep`, ADR-012.
8. **The database never changes an aggregate on behalf of something outside it** (Spec Phase 2,
   amended 2026-11-09). `AggregateReferenceConvention` (after the boundary convention) gives
   `DeleteBehavior.Restrict` to every foreign key whose dependent lies in an aggregate (a root, or an
   entity inside a boundary) and that is not one of the boundary's own links (a child's key to its
   parent, an ownership, a one-to-one): a reference to another root — of any type — or to an entity
   outside every boundary. It replaces EF Core's `Cascade` default for a required relationship
   ([Learn](https://learn.microsoft.com/ef/core/saving/cascade-delete#configuring-cascading-behaviors)).
   An explicit `Cascade` or `SetNull` on such a key fails model building, naming it: deleting a `Tag`
   would otherwise delete or modify rows of `Order` aggregates without bumping them. Other explicit
   behaviours are kept; relationships inside one boundary keep theirs. Existing cross-aggregate keys
   change from `CASCADE` to `RESTRICT` (`NO ACTION` on SQL Server): a migration.

### Consequences

- Every root's table needs a migration: a `NOT NULL` column, scaffolded with `defaultValue: 0L`;
  existing rows start at 0 and their first change takes them to 1.
- **Known limit — bulk writes bypass the version.** `ExecuteUpdate`, `ExecuteDelete` and raw SQL do
  not go through the change tracker: they write an aggregate's rows without bumping its version and
  without conflicting with a concurrent command.
- **Known limit — application interceptors.** EF Core runs interceptors registered in the internal
  service provider (MicroKit's) before those added with `AddInterceptors` (pinned by
  `AnInterceptorAddedByTheApplication_RunsAfterTheVersionStep`): a change an application interceptor
  makes during `SavingChanges` is not bumped. MicroKit's own steps keep their fixed order.
- **No shared rows.** ADR-009's per-row uniqueness (amended 2026-11-09) refuses, at model building, the
  shapes through which one row could sit inside several aggregate instances (a many-to-one reference
  held by the parent to a non-root; any many-to-many navigation inside a boundary not marked outside).
  A principal-side child is therefore a one-to-one with a unique foreign key, and a change to it bumps
  its single root. Links an aggregate owns are a link entity, a child: adding or removing one bumps the
  root; a skip navigation marked outside is not versioned.
- Keys are matched with EF Core's key value comparers (`IProperty.GetKeyValueComparer()`), so a
  `byte[]` key or a key with a custom comparer resolves like the change tracker resolves it.
- A delete is version-checked: `DeleteAsync` then a commit fails when another command changed the
  aggregate since it was loaded.
- A conflict stays a `PersistenceException` wrapping `DbUpdateConcurrencyException` (frozen); its
  typed form is the `CONCURRENCY` error of ADR-011. A conflict a repository detects itself (a staged
  root found with an expected version other than 0, the in-memory double's check at the find) is a
  `PersistenceException` wrapping `AggregateVersionConflictException` (phase 3; it derives from the
  `InvalidOperationException` it was).
- Other modules that use `UseMicroKitPersistence()` get the column on their roots and adapt during
  their own upgrade.
- Tests: `AggregateVersionTests` (PostgreSQL and SQL Server), `InMemoryRepositoryVersionTests`.

---

## ADR-011: Persistence Failures as `Result` Errors, Converted After the Rollback (#197 phase 3)

**Status:** Accepted
**Date:** 2026-11-09
**Source:** Issue #197, phase 3 (Spec amended 2026-11-09: placement, a failed `Result` rolls back,
SQL Server 547). Builds on ADR-006 (transactions), ADR-009 (one root per entity type) and ADR-010
(versions).

### Decision

1. **Three failures, one category.** Unique-constraint violations (`DUPLICATE`), foreign-key
   violations (`REFERENCE`) and concurrency conflicts (`CONCURRENCY`) map to `MicroKit.Result` errors
   of category `Conflict`, built with `Error.Conflict(code, message)`. Every other failure keeps
   throwing `PersistenceException` (sealed, frozen); `IUnitOfWork.CommitAsync` and
   `ITransactionalContext` are unchanged, and no member is added to them.
2. **Placement.** The contract (`IResultUnitOfWork`: `TryExecuteAsync` ×2, `TryCommitAsync`), the codes
   and messages (`PersistenceErrors`) and the version-conflict marker
   (`AggregateVersionConflictException`) live in `MicroKit.Persistence` (core), which gains the
   `MicroKit.Result` reference; `MicroKit.Persistence.Abstractions` still does not reference it (#196
   D17). A handler depends on the contract without referencing EF Core. `EfUnitOfWork` implements it;
   so does `InMemoryUnitOfWork`, using the same codes and marker — no copy. The names differ from
   `ExecuteAsync`/`CommitAsync` on purpose: an `ExecuteAsync` overload taking `Task<Result>` would
   capture existing callers of `ExecuteAsync<TState, TResult>` on their next compile.
3. **Codes.** `PERSISTENCE.<ROOT>.<REASON>`. `<ROOT>` is the error name of the failing entity's
   top-most aggregate root, found from the entity type alone through `AggregateBoundaryMap` (ADR-009
   makes it unique; the tracker is already cleared by the rollback). The default is the simple CLR name
   in UPPER_SNAKE (`PersistenceErrors.DefaultErrorName`); `HasAggregateErrorName` fixes it (annotation
   `MicroKit:ErrorName`, resolved by `AggregateErrorNameConvention`). Model building fails on two roots
   with one name, on the annotation on a type that is not a top-most root, and on an invalid default
   name. Failing entries spread over several roots, or outside every boundary, or no entries at all,
   give `PERSISTENCE.<REASON>`: a root is never guessed. The messages are fixed and client-safe — no
   table, constraint or column name; the provider exception stays in EF Core's log.
4. **Recognition, never by message.** An internal mapper (EntityFrameworkCore) reads the exception
   chain: `DbUpdateConcurrencyException` or `AggregateVersionConflictException` → `CONCURRENCY`; a
   `DbUpdateException` over a `DbException`, or a bare `DbException`, classified by the context's
   `IDatabaseFailureClassifier` → `DUPLICATE`/`REFERENCE`; a chain holding a
   `RetryLimitExceededException` → never. The classifiers live in the provider packages
   (`PostgreSqlFailureClassifier`: SQLSTATE `23505`, `23503`; `SqlServerFailureClassifier`: `2627`,
   `2601`, `547`), so `MicroKit.Persistence.EntityFrameworkCore` references neither Npgsql nor SqlClient;
   `UsePostgreSQL`/`UseSqlServer` configure them, and `UseMicroKitPersistence(classifier)` serves any
   other configuration. The classifier is read from the context's options, not from a service, so it
   takes no part in the internal service provider's key.
5. **SQL Server 547** is a foreign-key or a check violation. It is decided from the model only: it maps
   to `REFERENCE` when no entity type mapped to the failing entries' tables declares a check constraint
   (`GetCheckConstraints()`, on the design-time model); otherwise, or with no failing entries, it is not
   converted. A check constraint that exists only in the database is a documented limit. PostgreSQL's
   `23514` is never converted.
6. **Convert at the owner, after the rollback.** EF Core rolls a failed `SaveChanges` back to its own
   savepoint and leaves the transaction usable
   ([Learn](https://learn.microsoft.com/ef/core/saving/transactions#savepoints)): a failure converted
   inside the operation would let it return normally, and the owner would commit what the transaction
   wrote before — an earlier flush, an `ExecuteUpdate`, raw SQL, outbox rows.
   - `TryExecuteAsync` owning the transaction runs the operation through `ExecuteAsync`'s own core
     (strategy, transaction per attempt, transient retries, tracker cleared after a failed attempt) and
     converts a `PersistenceException` only once that core has thrown, i.e. after the rollback and once
     the strategy gave up. Unconverted failures propagate unchanged.
   - Joined to an open transaction, it runs the operation inside it and converts nothing: the exception
     reaches the owner, which rolls back; a failed `Result` returns unchanged and the owner decides.
   - `TryCommitAsync` is for code with no open transaction (a flush is atomic by itself); inside one it
     throws `InvalidOperationException`.
   - No mutable rollback-only flag exists: nothing can be converted while the transaction is alive.
7. **A failed `Result` the operation returns** is a business failure, not a persistence one: it is
   returned unchanged, and when `TryExecuteAsync` owns the transaction it rolls it back instead of
   committing, so a failed command never persists part of its work. The rollback uses an internal
   non-transient sentinel thrown inside the strategy's delegate and caught after the strategy returns —
   no flag; provider strategies retry provider exceptions only, so it is not retried (a custom strategy
   retrying every exception would re-run the operation: documented). The tracker is cleared, as after
   any failed attempt.
8. **The in-memory double** mirrors the rules: an operation of `TryExecuteAsync` stands for the
   transaction; the commits it makes count only on success; `AggregateVersionConflictException` from
   `InMemoryRepository` becomes `CONCURRENCY` with the default name; a nested call converts nothing;
   `TryCommitAsync` is refused inside an operation. It cannot roll its store back and has no model
   (`HasAggregateErrorName` is unknown to it).
9. **Ambient transactions** stay rejected, as EF Core rejects them (ADR-006 D4, amended).

### Consequences

- Model building fails on a root-name clash: a break for models with two roots of one simple name.
- `MicroKit.Persistence` (core) depends on `MicroKit.Result`; the root graph already allowed
  Persistence → Result.
- A context without a classifier converts concurrency conflicts only.
- `IResultUnitOfWork` does not extend `IUnitOfWork`: the operation flushes with `CommitAsync`, so a
  handler injects both contracts (both implementations implement both).
- MicroKit.MediatR's `TransactionBehavior` owns the transaction (`ExecuteAsync`), so inside its pipeline
  `TryExecuteAsync` is joined and converts nothing, and `TryCommitAsync` is refused. MicroKit.MediatR
  adopts the `Result` path during its own upgrade (Spec perimeter: no change, no issue here).
- The in-memory double converts only its version conflicts, and only inside `TryExecuteAsync` (it checks
  at the find); it never returns `DUPLICATE` or `REFERENCE`.
- Tests: `PersistenceResultTests` (PostgreSQL and SQL Server), `InMemoryResultUnitOfWorkTests`,
  `PersistenceErrorsTests`, `ContractPlacementTests`, `LayerDependencyTests`.

---

## ADR-012: Soft Delete of Whole Aggregates (#197 phase 4)

**Status:** Accepted
**Date:** 2026-11-09
**Source:** Issue #197, phase 4. User decisions 2026-11-09: the mark is `DateTimeOffset? AggregateDeletedAt`;
the opt-in is `HasSoftDelete()`; every tracked entry of the boundary is marked, whatever its state; owned
entries are restored without a mark; the mark is `TimeProvider.GetUtcNow()`; a required relationship that
does not cascade is refused at model building; a key into the aggregate from outside it is
`ClientNoAction`. Builds on ADR-009 (one root per entity type, the loaded-root
record) and ADR-010 (one save pipeline, the version step last).

### Decision

1. **Opt-in per aggregate, in the model.** `HasSoftDelete()` sets the annotation `MicroKit:SoftDelete` on a
   top-most root; anywhere else model building fails. `MicroKit.Domain` is unchanged.
2. **The mark and the filter.** `SoftDeleteConvention` (a finalizing convention, second after the version
   convention so naming conventions apply) gives the root type of every hierarchy of the aggregate — the
   root's and those the boundary walk reaches; an owned type follows its owner — the shadow
   `DateTimeOffset? AggregateDeletedAt` and the named filter `MicroKit.SoftDelete`
   (`EF.Property<DateTimeOffset?>(e, "AggregateDeletedAt") == null`). A hierarchy root is the only place EF
   Core accepts a filter — a mapped base type that is no aggregate root included. Named filters already
   there are kept. The name is reserved like the version's (CLR member, type, a column shared with another
   property of a table that stores the mark — each concrete type's under TPC — except the mark of another
   type of the same aggregate split onto the same table), and only
   its column may be configured: a required mark, a default or computed value, value generation or a
   converter fails model building (a default would make every new row deleted).
3. **Model refusals.** An anonymous filter on an entity of the aggregate fails model building, naming the
   entity and the fix (name the other filter): EF Core 10 rejects an anonymous filter combined with a named
   one. A required foreign key between two entity types of the aggregate (ownership keys aside) configured
   `Restrict`, `NoAction`, `ClientSetNull` or `SetNull` fails model building, naming the key and the fix
   (`Cascade`, or `ClientCascade`), whether or not a navigation of the boundary walk reaches it: under EF
   Core's default `Immediate` cascade timing, `Remove(root)` throws for such a key at once ("the
   association … has been severed", pinned by `SoftDeleteFoundationTests`), inside `DeleteAsync`, before
   any save step can run. Keys count through inheritance: one a member inherits from a mapped base type, and
   one whose principal is a mapped base type of a member (a base of the root holding its collection). A key
   declared on a mapped base type of a child that is not itself in the boundary is a key from outside (below):
   an explicit behaviour on it fails there. `Cascade`, `ClientCascade` and `ClientNoAction` are accepted; `ClientNoAction`
   leaves the child untouched, so the step marks it (a required `ClientNoAction` child is tested on both
   servers).
   - **A key into the aggregate from outside it** — its principal is the root or an entity of its boundary,
     its dependent another aggregate or an entity outside every boundary, join rows of a many-to-many marked
     `IsOutsideAggregate()` included — is `DeleteBehavior.ClientNoAction` by convention
     (`AggregateReferenceConvention`), replacing ADR-010's `Restrict` for those keys; an explicit other
     behaviour fails model building, naming the key. Reason: a soft-deleted principal is never deleted, and
     with any other behaviour EF Core reacts to `Remove(root)` on a *tracked* dependent only — nulls an
     optional key and writes `NULL`, throws for a required one, deletes a cascading one. The database
     constraint stays a no-action one (`NO ACTION`; on PostgreSQL `RESTRICT` becomes `NO ACTION`: a
     migration); a real delete of a referenced child still fails in the database, and the `Result` path
     returns `REFERENCE`.
4. **When the step runs** (EF Core 10 source, `release/10.0`). `DbContext.SaveChangesAsync` runs the
   `SaveChangesStartingAsync` interceptors, then `TryDetectChanges`, then `StateManager.SaveChangesAsync`;
   only there does `GetEntriesToSave(cascadeChanges: true)` call `CascadeChanges`, which first handles
   conceptual nulls (`HandleConceptualNulls`, which throws for a required key that is not cascading) and
   then runs `CascadeDelete` for every entry still `Deleted`. So when the step runs, a boundary child may
   still be `Unchanged` (`ClientNoAction`; `CascadeDeleteTiming` or `DeleteOrphansTiming`
   `OnSaveChanges`), already `Deleted` (`Immediate` cascade), `Modified` with a nulled foreign key
   (`ClientSetNull`), or carry a conceptual null. Pinned by `SoftDeleteFoundationTests` (SQLite, without
   MicroKit): a `SavingChanges` interceptor sees `Unchanged`, `Modified` or `Deleted` per configuration.
5. **The step.** `AggregateSoftDeleteStep`, before the version step:
   - the soft-deleted roots are the `Deleted` top-most roots of opted-in aggregates; one the record does not
     hold (ADR-009) fails the save — its untracked entities could not be marked;
   - **every tracked entry** of their boundaries is marked, whatever its state. An entry's root is resolved
     through the boundary map by its **current** keys first — even for a `Deleted` entry (a child moved
     into the deleted root and cascaded with it is marked; one moved out to a live root and removed there is
     a real delete) — and by its original keys only when its current keys name no tracked root (a key EF
     Core nulled, a child let go). For a principal-side child, only the parent whose current (or, in the
     fallback, original) key names it counts: a contact the deleted root let go and a live root took is not
     marked. A conceptual null still reads the original value, so the current key finds the root.
     Everything is computed before anything is changed;
   - for each marked entry: a nulled foreign key gets its original value back, and writing the value also
     clears a conceptual null (`PropertyEntry.CurrentValue = original`, pinned); a `Deleted` entry becomes
     `Unchanged`, and every property whose value differs from its original is flagged modified again, so a
     change made before the delete is written; then the mark is set. An `Added` entry is inserted marked;
   - an **owned** entry of a marked entity (its owner chain) has no mark column: it is restored the same way
     without a mark, while its owner still holds it — otherwise a table-split `OwnsOne` would be written as
     an `UPDATE` that nulls its columns, and an `OwnsMany` in its own table would be deleted. An owned value
     the command replaced or removed stays deleted; one it added, which `Immediate` cascade timing detached
     on `Remove(root)`, is tracked again as `Added` with its owner's key, so the replacement is written;
   - complex properties are re-flagged too, so a value-object change made before the delete is written
     even with automatic change detection off;
   - **the mark** is `TimeProvider.GetUtcNow()` — offset zero, which Npgsql requires for `timestamptz` —
     one instant per save shared by every entry, from the context's application services
     (`CoreOptionsExtension.ApplicationServiceProvider`), else `TimeProvider.System`;
   - no entry of the boundary is left `Deleted`, so `CascadeChanges` finds nothing to cascade or reject for
     it; the version step then bumps the root, now `Modified`, and checks it (`WHERE AggregateVersion = @original`).
6. **After the commit** the marked and restored entities still tracked are detached and the root leaves the
   record, as a hard delete detaches — **once the save accepted its changes**. EF Core 10 runs
   `AcceptAllChanges` inside `StateManager.SaveChangesAsync`, before `SaveChangesCompleted` (the
   `SavedChanges` interceptors), and only when `acceptAllChangesOnSuccess` is `true` (`DbContext.cs`,
   `StateManager.cs`, `release/10.0`). After `SaveChanges(acceptAllChangesOnSuccess: false)` — the pattern of
   a save retried by hand in a transaction (Learn: connection resiliency, execution strategies and
   transactions) — the marked entries are still `Added` or `Modified`: they stay tracked and the root stays
   recorded, so the save retried after a rollback writes the mark (tested on both servers). Acceptance covers
   a whole save, so the decision is all or none: while any of them is still changed, none is detached; the
   time they are all `Unchanged` — after a save that accepts, or after `AcceptAllChanges()` (EF Core's
   resilient pattern: `SaveChanges(false)`, commit, `AcceptAllChanges()`, which raises no `SavedChanges`) — they
   are detached: after every successful save, and before `EfRepository.FindAsync` and `LoadAggregatesAsync`
   look for a root, so the write side never returns the soft-deleted root (tested on both servers). The list lives on the scoped `AggregateLoadRecord` and is cleared with
   it (`DiscardChanges`, a failed `ExecuteAsync` attempt, a pooled lease's reset), so nothing of a cleared
   tracker is ever detached later. A failed save leaves the entries marked and `Modified`: a later save in
   the same scope writes those marks as they are (the step does not run again, and an entity attached to
   the aggregate in between is not marked).
7. **What stays a real delete:** a child removed from an aggregate that stays alive, an aggregate without
   `HasSoftDelete()`, bulk `ExecuteDelete` and raw SQL.
8. **Reads.** `QueryOptions<T>.IncludeDeleted` (`WithDeleted()`, core) makes `EfSpecificationEvaluator`
   apply `IgnoreQueryFilters([SoftDelete.FilterName])` first: the named overload, so every other filter —
   a tenant's — still applies; the parameterless overload stays banned (`IlCallScanTests`). The write side
   and `FindAsync(keyValues)` never see a soft-deleted aggregate; there is no undelete.

### Consequences

- A migration per opted-in aggregate: a nullable column on each of its tables.
- A unique index still sees soft-deleted rows. The README shows the recipe — a filtered unique index,
  `HasFilter("\"AggregateDeletedAt\" IS NULL")`, valid on PostgreSQL and on SQL Server (filtered indexes
  run with `QUOTED_IDENTIFIER ON`) — and no convention rewrites the application's indexes.
- A required navigation from another aggregate to a soft-deleted root is hidden by the filter (EF Core's
  `PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning`).
- `MicroKit.Tenancy`'s anonymous tenant filter must be named before a tenant entity can be soft-deletable;
  Tenancy names it during its own upgrade (Spec: no issue).
- The in-memory doubles delete for real.
- Tests: `SoftDeleteTests` (PostgreSQL and SQL Server), `SoftDeleteFoundationTests` (EF Core facts).

---

## ADR-013: Read Side — Projections, Keyset Pagination, Compiled Finds (#197 phase 5)

**Status:** Accepted
**Date:** 2026-11-09
**Source:** Issue #197, phase 5. User decisions 2026-11-09:
- the cursor is an opaque Base64Url token built on provider values, with a fingerprint of the root's model name
  and the ordering, a `TryParse` that checks the format only, and untrusted-input bounds;
- **paging is forward only** — the user's decision, not an assumption of this ADR.

Builds on ADR-002 (`QueryOptions`), ADR-009 (the read side never tracks) and ADR-012 (the soft-delete filter).

### Decision

1. **Projections.** `IReadRepository<T>` gains `ListAsync<TResult>(opts, selector)` and
   `ListPagedAsync<TResult>(opts, selector)`. The database builds the DTO: the specification, the ordering
   and the pagination apply, then `AsNoTracking().Select(selector)`. The includes are dropped, since the
   selector says what is read; the split hint is kept: it splits the collections the selector projects, which
   would otherwise come back as one cartesian product. Step 0 (`IncludeDeleted`) still applies.
2. **Keyset pagination.** `KeysetPagination<T>` (core) is set on `QueryOptions<T>.Keyset` by `WithKeyset`:
   - the sort keys (`OrderBy`/`ThenBy[Descending]`), a `PageSize` (the `PaginationLimits` default when
     absent, the maximum enforced) and `After`, a cursor;
   - `ListKeysetAsync(opts)` and `ListKeysetAsync<TResult>(opts, selector)` read a page;
   - the keyset owns the ordering and the page: `OrderBy` or `Pagination` set alongside it is refused, and
     `ListAsync`, `ListPagedAsync`, `AnyAsync` and `CountAsync` refuse options carrying a keyset, rather than
     drop its cursor silently (`KeysetRules`, shared by the module's implementations);
   - **forward only**: a page returns `Next`, `null` on the last page; there is no total count.
3. **A total ordering.** The root's primary-key properties are appended, ascending, to the sort keys (those
   not already among them), so ties are broken and no row is skipped or repeated.
4. **Sort keys.** Each sort key must be:
   - a scalar property of the root (a member access or `EF.Property` on the root parameter, a `Convert` to an
     interface the root implements unwrapped, found by `IEntityType.FindProperty`) — anything else is refused,
     naming the key; a primitive collection is refused too (on SQL Server it is a JSON `string`, which the
     cursor would otherwise accept);
   - not nullable: `Nullable<T>` is refused when the key is built, and a nullable column (`IProperty.IsNullable`)
     when the page is read;
   - stored as a provider type the cursor carries — after the property's value converter, so a strongly typed
     id stored as a `Guid` is fine; another provider type fails with `InvalidOperationException` naming the
     property.
5. **The page query.** EF Core translates the start of a page as
   `k1 >= v1 AND ((k1 > v1) OR (k1 = v1 AND ((k2 > v2) OR (k2 = v2 AND …))))`, with `<=` and `<` for a
   descending key. The leading bound is the range an index on the keys seeks; the `OR` chain alone is not one.
   - Each comparison is a `BinaryExpression` that carries a generic helper method. EF Core translates the node
     to the SQL operator whatever the key type's C# operators: a value-converted record-struct id, `Guid`,
     `string`, `DateTimeOffset` and `bool` were pinned on PostgreSQL and SQL Server (`ReadSideFoundationTests`).
   - The cursor's values sit in a holder, so the query is parameterised and its cache entry stable.
   - The plan — the resolved keys, their generic methods, the fingerprint and the root-only row projection — is
     built once per root entity type and ordering, and cached.
   - `Take(pageSize + 1)` is a constant, as offset pagination's `Skip`/`Take` are: one query-cache entry per page
     size, bounded by `PaginationLimits.MaxPageSize`.
   - The query orders by every key, takes `pageSize + 1` rows to know whether a page follows, and projects each
     row as `KeysetRow(object[] keys, item)`. The cursor therefore comes from the same rows as the items, a
     projection included, and an `Includes` delegate still applies to a page of aggregates (pinned).
6. **The cursor** (`KeysetCursor`, core). Its public surface is `ToString()`, `TryParse(string, out cursor)`
   and `MaxTokenLength`. The codec and the fingerprint are internal, shared with the EF Core and Testing
   packages through `InternalsVisibleTo`: they ship with core under one version.
   - **Format.** Base64Url of version `1`, an 8-byte fingerprint (the first bytes of a SHA-256 over the root's
     model name and each key's name and direction, each name length-prefixed), a value count, then tagged
     values from a closed set of provider types: the integers, `bool`, `float`, `double`, `decimal`, `Guid`,
     `string` (UTF-8), `DateTime` (`ToBinary`), `DateTimeOffset` (ticks and offset), `DateOnly`, `TimeOnly`
     and `byte[]`.
   - **Encoding.** No reflection and no serializer: `BinaryPrimitives` and UTF-8 only, so the codec is
     trim-safe without suppression.
   - **Untrusted input.** `TryParse` refuses:
     - a token above `MaxTokenLength` (1024 characters);
     - an invalid Base64Url string (checked with `Base64Url.IsValid`: `TryDecodeFromChars` throws on an invalid
       character);
     - a token that does not re-encode to itself;
     - an unknown version or tag, trailing bytes, an out-of-range value, or invalid UTF-8.

     Every declared length — a string's, a `byte[]`'s, the value count — is checked against the bytes that
     remain before anything is allocated for it. A forged token declaring `int.MaxValue` bytes is refused
     without that allocation (tested by measuring the bytes allocated).
   - **Checked at query time.** A fingerprint of another root (a token issued for `Order` on an `Invoice` list,
     even with the same key names and provider types) or of another ordering is refused, and so is a value
     whose type is not its key's provider type. Both throw `ArgumentException`. Values go provider → model
     through each key's converter.
   - The token is not encrypted: a client can read the sort-key values of the last row of its page.
7. **Compiled finds** (`CompiledFinds`). The hot paths are the two finds by id.
   - `EfRepository.FindAsync(TId)` runs the whole-aggregate load as `EF.CompileAsyncQuery`. It has the includes
     of `AggregateIncludePlan`, its split decision, tracking and the key predicate, and it is materialised in the
     same record scope (ADR-009).
   - `EfReadRepository.FindAsync(keyValues)` for a single-property key runs an untracked compiled
     `FirstOrDefault`, made for the key's CLR type. A composite key, a list, a count and an any stay dynamic:
     their shape changes with each call, and EF Core's query cache serves them.
   - **One delegate per model.** EF Core 10's `CompiledQueryBase` binds a compiled query to the first model it
     runs with, and throws `CompiledQueryDifferentModel` on another (pinned): one context type over two
     providers, or a model cache key with variants. Each delegate is cached per `IModel` in a
     `ConditionalWeakTable`.
   - **Query filters.** A compiled query keeps the model's query filters, and evaluates the `DbContext` members a
     filter reads at each execution: a tenant filter reads the executing context's tenant (pinned without
     MicroKit, and tested through both finds on both servers).
8. **The doubles.** `InMemoryReadRepository` and `InMemoryRepository` compile the selector of a projection, and
   order a keyset page in memory: strings ordinally, a key that is not `IComparable` by its string form, with
   the store's key appended. They refuse a nullable sort key through the member's nullability annotations.
   They have no value converters, so a double's cursor carries a handle into the double, in the same format
   and with the same fingerprint: it is valid only on the double that issued it.

### Consequences

- `IReadRepository<T>` gains four members: a break for an implementation outside the module. Such an
  implementation cannot issue or read a cursor, whose codec is internal: it forwards keyset pages to one of the
  module's implementations, or throws `NotSupportedException`. The internals shared by `InternalsVisibleTo` are a
  contract between the module's assemblies, which ship under one version.
- `KeysetPagination`, `KeysetKey`, `KeysetCursor` and `KeysetPage` are new public types in core.
- A keyset ordering by a column whose provider type is outside the set (`TimeSpan`, a JSON column) is not
  supported; sort by another property or convert it.
- Tests: `ReadSideTests` (PostgreSQL and SQL Server), `ReadSideFoundationTests` (EF Core facts),
  `KeysetCursorTests`, `KeysetPaginationTests`, `InMemoryReadSideTests`.

---

## ADR-014: Query-Shape Analyzers MKP006–MKP009, Without Code Fixes (#197 phase 6)

**Status:** Accepted
**Date:** 2026-11-09
**Source:** Issue #197, phase 6. Its Spec:
- names the four shapes;
- sets the collection `Include` analyzer at two or more collection includes in one query, as a suggestion
  (`Info`) with no code fix ("one collection is a single `LEFT JOIN`, with no cartesian product; EF Core's
  `MultipleCollectionIncludeWarning` and the module's own split also start at two");
- ships a code fix only where it preserves semantics, and records the reason for each analyzer without one.

User decision 2026-11-09: a third diagnostic category, `MicroKit.Persistence.Performance`, for MKP006–MKP008.

Builds on ADR-009 (the aggregate load's split), ADR-012 (named soft-delete filter) and ADR-013 (keyset pages).

### Decision

1. **Four diagnostics** in `MicroKit.Persistence.Analyzers`:

   | ID | Analyzer | Category | Severity |
   |---|---|---|---|
   | MKP006 | `CollectionIncludeAnalyzer` | Performance | Info |
   | MKP007 | `QueryInLoopAnalyzer` | Performance | Warning |
   | MKP008 | `MaterializeBeforeFilterAnalyzer` | Performance | Warning |
   | MKP009 | `IgnoreAllQueryFiltersAnalyzer` | Usage | Warning |

2. **Precision over recall.** What an analyzer cannot see, it does not guess: it stays silent. A missed report
   costs one review; a false one teaches teams to suppress the rule.
   - **MKP006 and MKP008** judge a whole query chain in one expression, from its root (a `DbSet`, `Set<T>()`, or
     for MKP006 the lambda handed to `QueryOptions.WithIncludes`/`Includes`) to its execution. A query composed
     over several statements is not seen.
   - **MKP007** judges each executing call where it stands, on any query or repository.
   - **None of them sees** the model, the global `UseQuerySplittingBehavior`, or a typed finder declared outside
     a repository type.
3. **Symbols** are resolved once per compilation, by metadata name, through `PersistenceSymbolHelper`. Each rule
   group is gated on its own symbols (EF Core, relational, the MicroKit contracts). So MKP007's repository rule runs
   in an application project that references MicroKit core without EF Core, and MKP006 is silent on a provider
   without `AsSplitQuery` (Cosmos).
4. **MKP006 — two or more collections in one query.**
   - **What it counts.** The `Include`/`ThenInclude` lambda overloads whose navigation type is a collection
     (`string` excepted; a filtered include counts), deduplicated by navigation path. `Include(string)` names no
     navigation the analyzer can see.
   - **Where it reports.** On the include of the second collection, when neither `AsSplitQuery()` nor
     `AsSingleQuery()` is in the chain.
   - **Ignored includes.** EF Core ignores the includes of a query that returns no entity, so the analyzer is
     silent after a projection (`Select`, `SelectMany`, `GroupBy`, a join) or before a scalar executor (`Count`,
     `Any`, `All`, `Sum`, `Min`, `Max`, `Average`, `Contains`, their async forms).
   - **QueryOptions.** The options' `AsSplitQuery()` or `AsSplitQueryEnabled = true` settles it, in the same
     expression, before or after `WithIncludes`. Options passed inline to `CountAsync`, `AnyAsync` or a projection
     drop the includes, so they are silent too.
   - **Options built over several statements.** Unlike a raw EF query, options are reported without being seen
     executed: they exist to be read. Options split in a later statement are reported, which an `Info` diagnostic
     tolerates.
   - **One collection is not reported.** It is a single `LEFT JOIN`, and the module's own load keeps it as one query
     (`AggregateIncludePlan` splits from two collections) — the Spec's threshold.
5. **MKP007 — a query on each iteration of a `foreach`, `await foreach` or `for` loop.**
   - **What counts as a query:**
     - a LINQ operator that executes a query: `Queryable` returning no query; `Enumerable` materializers and scalars
       over one;
     - EF Core's async executors and `Load`;
     - `Find`/`FindAsync`;
     - explicit loading (`NavigationEntry.Load*`);
     - a nested `foreach` over a query;
     - the awaitable methods of a MicroKit repository, typed finders included.
   - **Not reported:**
     - composition (a loop that only builds the query: `query = query.Include(path)`);
     - the staging verbs (`Add*`, `Delete*`, `Remove*`);
     - `ListKeysetAsync`/`ListPagedAsync`, called once per page by design;
     - LINQ to objects through `AsQueryable()`;
     - EF Core's `Execute*` bulk writes.
   - **Where in the loop:**
     - the body, a `for` condition and its incrementors run on each iteration;
     - a `foreach` collection and a `for` initializer run once, for that loop only (the search goes on outward);
     - a lambda or local function stops the search: when it runs is unknown;
     - `while` and `do` loops, and a `for` loop without a condition, are the batching, polling and retry idioms, and
       do not count.
6. **MKP008 — materialized, then filtered.** It reports `ToList()`/`ToArray()` on a query, or an awaited
   `ToListAsync()`/`ToArrayAsync()`, that is immediately the receiver of a filter with an expression-bodied predicate.
   The filter is `Where` (not its index overload), or the predicate overloads of `First*`, `Single*`, `Any`, `All`,
   `Count`, `LongCount`, and `Last*` only after an `OrderBy`, which EF Core requires. It reports only when the filter
   can follow the materialization into the query:
   - **The query commutes with a filter.** It is rooted at a `DbSet`, and only operators a filter moves past lie
     between root and materializer (`Where`, the orderings, the includes, the tracking and split hints,
     `IgnoreQueryFilters`, `TagWith`). A `Take`, `Skip`, `Distinct`, projection, grouping or join changes what a
     filter after it means.
   - **The predicate plainly translates.**
     - Every part reading the element is one of:
       - a single hop to a property with a public getter, a setter, no `[NotMapped]` and a scalar type;
       - a literal;
       - a built-in or scalar-type operator;
       - a built-in conversion, `??` or `?:`;
       - `string.Contains`/`StartsWith`/`EndsWith(string)`;
       - a `Contains` over a captured collection.
     - Every other part is captured: EF Core evaluates it and sends it as a parameter. A captured part is limited
       to locals, parameters, fields, properties, calls with explicit arguments, literals, operators, conversions
       and arrays; anything else there (`?:`, `??`, `new`, an interpolated string) makes the rule silent.
     - A deeper hop (`o.Id.Value` reads inside a value-converted `GuidId`) is silent, and so is any call on the
       element, a user-defined operator, a pattern, `?.`, or a block body.
   - **Not reported.** `AsEnumerable()` before the filter is the explicit in-memory idiom.
7. **MKP009 — the parameterless `IgnoreQueryFilters()`.** It reports the invocation and the method group, an
   expression tree included. It is silent on the named overload (the evaluator's
   `IgnoreQueryFilters([SoftDelete.FilterName])`), `WithDeleted()` and `nameof`. It is a Warning, not an Error,
   because an anonymous query filter has no name: the parameterless overload is the only way to ignore one.
   `IlCallScanTests` stays the module's own guard, over its assemblies' IL.
8. **No code fix ships.** No rewrite of these shapes provably preserves semantics:
   - **MKP006**:
     - `AsSplitQuery()` turns one snapshot into 1 + C reads, which disagree under concurrent writes, and with
       `Skip`/`Take` page differently unless the ordering is unique;
     - `AsSingleQuery()` only silences the rule, and overrides a global split behaviour the analyzer cannot see.
   - **MKP007**. Batching the loop into one `Contains` query changes:
     - the order and the duplicates of the results;
     - missing keys (`null` per iteration, against an absent row);
     - the state each iteration observes when the loop writes;
     - when side effects run relative to the reads.
   - **MKP008**. Moving the filter into SQL changes its result:
     - a tracking query's in-memory filter sees the unsaved values of instances the context already tracks, where
       SQL sees the stored rows;
     - SQL Server's default collation compares strings case-insensitively, and PostgreSQL uses the column's
       collation;
     - `StartsWith(string)` is culture-sensitive in memory, unlike `LIKE`;
     - a captured call runs once per element in memory, once in SQL;
     - precision is the column's;
     - a translation EF Core lacks fails at run time.

     The analyzer cannot check any of these; the developer must.
   - **MKP009**. The analyzer cannot see which filters the model has, so it cannot name them; inserting
     `[SoftDelete.FilterName]` or `WithDeleted()` changes which rows return, and that choice is the developer's.

   The package therefore stays diagnostic-only: no `Microsoft.CodeAnalysis.CSharp.Workspaces`, no code-fix project.

### Consequences

- MKP007–MKP009 are warnings: a build with `TreatWarningsAsErrors` reports them as errors. Where a loop means its
  queries, or a model configures splitting globally, `.editorconfig` sets the severity
  (`dotnet_diagnostic.MKP006.severity = none`) or a `#pragma` suppresses one site.
- **Blind spots, by the precision rule.**
  - **What the analyzers cannot see:**
    - queries composed over several statements (MKP006, MKP008);
    - a global split behaviour;
    - `Task.WhenAll(ids.Select(…))`;
    - `while`, `do` and condition-less `for` loops;
    - a finder outside a repository type;
    - the model's mappings beyond the setter and `[NotMapped]` rules.
  - **Not reported on purpose:**
    - a single collection include, and includes EF Core ignores;
    - a materialization filtered in a later statement.
- The analyzers do not run on the module's own code (its src projects do not load them). Tests reproduce the
  module's internal shapes and assert they are silent:
  - `EfRepository.IncludeAggregate`: string includes in a loop, then the split decision;
  - `EfRepository.LoadAsync`: `await foreach` over the query;
  - `EfSpecificationEvaluator`: the named `IgnoreQueryFilters`;
  - `AggregateSave`: `ChangeTracker.Entries().ToList()`.
- Tests: `CollectionIncludeAnalyzerTests`, `QueryInLoopAnalyzerTests`, `MaterializeBeforeFilterAnalyzerTests`,
  `IgnoreAllQueryFiltersAnalyzerTests` (stubs in `Stubs/QueryStubs.cs`, .NET 9 reference assemblies).

---

## DN-001: Execution Strategy Coverage Gap in EfUnitOfWork.CommitAsync

**Status:** Superseded by ADR-006 (#196). Its observation was wrong: outside a transaction,
`SaveChangesAsync` already runs through the provider's execution strategy, so a direct `CommitAsync`
was retried. The real gap was the opposite path — inside `ExecuteAsync`, where `CommitAsync`'s
translation hid the `DbUpdateException` from the strategy. The original note is kept below.

**Original status:** Deferred (v1.1 candidate)
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

**Status:** Superseded by #196 D6. `ListPagedAsync` without pagination no longer returns the whole
result set as one page: it returns page 1 of `PaginationLimits.DefaultPageSize` items (50 unless
configured), so `PageSize` always reports the page size applied. The original note is kept below.

**Original status:** Deferred (v1.1 candidate)
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
