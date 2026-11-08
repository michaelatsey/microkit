# Rule: Testing — MicroKit.Persistence

## Test Projects

| Project | Tests |
|---------|-------|
| `UnitTests` | `QueryOptions` and its extensions, `PaginationOptions`, the in-memory doubles — no database |
| `IntegrationTests` | EF Core against SQLite in-memory (`EntityFrameworkCore/`), PostgreSQL via Testcontainers (`PostgreSql/`), provider registration (`Providers/`) |
| `ArchitectureTests` | NetArchTest layering, contract placement, sealing, namespaces; `IlCallScanTests` (Mono.Cecil) |
| `Analyzers.Tests` | MKP001–MKP005 — see `analyzers.md` |

There is no performance-test or benchmark project.

PostgreSQL tests use `PostgreSqlFixture` and `[DockerRequiredFact]` / `[DockerRequiredTheory]`,
which skip when Docker is unavailable, so `dotnet test` stays green without it. Transaction
semantics (retry, join, rollback — ADR-006) are tested on PostgreSQL only.

## Libraries

Root `.claude/rules/testing-libraries.md` governs: xUnit, Shouldly, NSubstitute, NetArchTest,
Testcontainers PostgreSQL; FluentAssertions is forbidden (commercial licence). Handler-level unit
tests use `MicroKit.Persistence.Testing`:

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

- Test classes are `sealed`; methods `Method_Scenario_ExpectedResult`.
- `[Fact]` for one case, `[Theory]` + `[InlineData]` for parameterised ones.
- A fix comes with a test shown to fail when the fix is reverted.
- Each SQLite test owns its connection; anything touching uniqueness, concurrency or transactions
  runs on PostgreSQL.
- Test `.csproj`: `GenerateDocumentationFile=false`, `NoWarn` includes `CS1591;CA1707`.

## Cases worth covering

- Write repository: find existing / missing, add then commit, find → mutate → commit, delete then
  commit, aggregate load includes the children (`EfRepositoryAggregateLoadTests`).
- Read repository: spec filter, ordering + pagination, default page and maximum size, any, count,
  `FindAsync` tracked vs untracked.
- Unit of work: commit translation to `PersistenceException`, `DiscardChanges`, cancellation.

## Detecting violations

```bash
grep -rn 'FluentAssertions' modules/MicroKit.Persistence --include="*.cs" --include="*.csproj"
```
