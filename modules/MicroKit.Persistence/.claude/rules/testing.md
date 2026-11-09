# Rule: Testing — MicroKit.Persistence

## Test Projects

| Project | Tests |
|---------|-------|
| `UnitTests` | `QueryOptions` and its extensions, `PaginationOptions`, `PersistenceErrors`, the in-memory doubles — no database |
| `IntegrationTests` | EF Core against SQLite in-memory, PostgreSQL and SQL Server via Testcontainers (`EntityFrameworkCore/`, `PostgreSql/`, `SqlServer/`), the Docker probe (`Docker/`), provider registration (`Providers/`) |
| `ArchitectureTests` | NetArchTest layering, contract placement, sealing, namespaces; `IlCallScanTests` (Mono.Cecil) |
| `Analyzers.Tests` | MKP001–MKP005 — see `analyzers.md` |

There is no performance-test or benchmark project.

PostgreSQL and SQL Server tests use `PostgreSqlFixture` / `SqlServerFixture` (collections
`PostgreSqlSuite` / `SqlServerSuite`) and `[DockerRequiredFact]` / `[DockerRequiredTheory]`. These
skip only when no Docker daemon answers `GET /_ping` (`DockerEndpoint`: a leftover socket file does
not count), so `dotnet test` stays green without Docker — but a skipped database test is a missing
proof: every phase of #197 ends with none skipped. Transaction semantics (retry, join, rollback —
ADR-006) are tested on PostgreSQL.

A feature tested on both servers is written once, in an abstract scenario class with an abstract
provider hook (`UseProvider`), and run by one sealed subclass per server in its own collection —
`AggregateBoundaryScenarios` → `AggregateBoundaryPostgreSqlTests`, `AggregateBoundarySqlServerTests`.

## Libraries

Root `.claude/rules/testing-libraries.md` governs: xUnit, Shouldly, NSubstitute, NetArchTest,
Testcontainers PostgreSQL and MsSql; FluentAssertions is forbidden (commercial licence).
Handler-level unit tests use `MicroKit.Persistence.Testing`:

```csharp
var orders = new InMemoryRepository<Order, OrderId>();   // also IReadRepository<Order>
var unitOfWork = new InMemoryUnitOfWork();               // counts CommitCount / DiscardCount

await orders.AddAsync(order);
await unitOfWork.CommitAsync();
(await orders.FindAsync(order.Id)).ShouldNotBeNull();
```

The doubles apply the same `PaginationLimits` rules as `EfReadRepository`; they do not support
composite keys, includes or tracking.

## Conventions

- Test classes are `sealed` (an abstract scenario class is the exception, see above); methods
  `Method_Scenario_ExpectedResult`.
- `[Fact]` for one case, `[Theory]` + `[InlineData]` for parameterised ones.
- A fix comes with a test shown to fail when the fix is reverted.
- Each SQLite test owns its connection; anything touching uniqueness, concurrency or transactions
  runs on PostgreSQL and SQL Server; every new feature lands with tests on both.
- Test `.csproj`: `GenerateDocumentationFile=false`, `NoWarn` includes `CS1591;CA1707`.

## Cases worth covering

- Write repository: find existing / missing, add then commit, find → mutate → commit, delete then
  commit, aggregate load includes the children (`EfRepositoryAggregateLoadTests`).
- Read repository: spec filter, ordering + pagination, default page and maximum size, any, count,
  `FindAsync` and `ListAsync` untracked even when the context tracks the root.
- Version (`AggregateVersionTests`): every kind of write inside a boundary bumps once; moves; roots
  not tracked; entities outside every boundary; two-context conflict; a real retried attempt; a
  stale and a current expected version; the model checks on the reserved name (configuration beyond the
  column, a shared column under TPC, an unrelated TPC table or sibling roots under one TPH base not a collision); link entities;
  cross-aggregate keys `Restrict` (deleting a linked or referenced root fails, an explicit
  `Cascade`/`SetNull` fails model building, a child still cascades with its root).
- Boundary and record (`AggregateBoundaryTests`): a child shared by two roots fails model building;
  `IsOutsideAggregate`; every way to track a partial root is refused; the record survives pooling,
  `DiscardChanges` and a failed `ExecuteAsync` attempt.
- Unit of work: commit translation to `PersistenceException`, `DiscardChanges`, cancellation.
- Soft delete (`SoftDeleteTests`, both servers): the whole boundary marked, no row gone, owned values kept,
  version bumped; a child removed from a live root deleted; removed in the same save, marked; two aggregates
  in one save; `OnSaveChanges` timings; `ClientSetNull`/`ClientNoAction` children; a moved child; an added
  child; child `DbSet` hidden; `WithDeleted` with another named filter; detached after the commit; the
  filtered unique index; not-loaded root refused; stale version; model refusals; a child moved in or out; a
  principal-side child taken by a live root; outside references `ClientNoAction` (tracked dependent kept,
  `REFERENCE` on a real delete); owned replacements; complex properties with detection off; retried,
  discarded and pooled failures; `SaveChanges(false)` rolled back then saved again, or accepted by hand; required keys through a mapped base type; a required `ClientNoAction`
  child marked; a required key without navigation refused; a mark column shared under TPC, and one of an unrelated
  TPC type allowed; table splitting; a mapped base type. EF Core facts:
  `SoftDeleteFoundationTests` (SQLite).
- `Result` path (`PersistenceResultTests`, both servers): DUPLICATE with neither flush persisted;
  REFERENCE; CONCURRENCY from a second context, a stale expected version and a staged root found at a
  version other than 0; the root of a child, no root for two aggregates or none; a fixed name; a failed
  `Result` rolls back; a joined call converts nothing; a transient retried; retries exhausted
  unconverted; `TryCommitAsync` refused in a transaction; ambient transactions rejected; SQL Server 547
  on a table with a check constraint unconverted; model name clashes. The double:
  `InMemoryResultUnitOfWorkTests`.

## Detecting violations

```bash
grep -rn 'FluentAssertions' modules/MicroKit.Persistence --include="*.cs" --include="*.csproj"
```
