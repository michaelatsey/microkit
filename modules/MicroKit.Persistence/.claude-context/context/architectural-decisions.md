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
