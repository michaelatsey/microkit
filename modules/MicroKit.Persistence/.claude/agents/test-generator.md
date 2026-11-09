---
name: test-generator
description: Use this agent to generate test suites for MicroKit.Persistence repositories, specifications, QueryOptions, and Unit of Work behaviour. Produces xUnit + Shouldly + NSubstitute tests — unit tests over the MicroKit.Persistence.Testing in-memory doubles, integration tests over SQLite, PostgreSQL or SQL Server (the Testing.PostgreSql / .SqlServer fixtures, Testcontainers).
tools: Read, Glob, Grep
model: sonnet
---

# Agent: Persistence Test Generator

## Stack
- **xUnit**, **Shouldly**, **NSubstitute**; FluentAssertions is forbidden (commercial licence)
- `InMemoryRepository<TAggregate, TId>` (write + read), `InMemoryReadRepository<T>`,
  `InMemoryUnitOfWork` (`CommitCount`, `DiscardCount`) from `MicroKit.Persistence.Testing`
- SQLite in-memory for EF Core behaviour; PostgreSQL and SQL Server via `PostgreSqlSuiteFixture` /
  `SqlServerSuiteFixture` (xUnit adapters over the published `PostgreSqlFixture` / `SqlServerFixture`; a per-test
  database is `fixture.GetConnectionString(database)`) + `[DockerRequiredFact]`/`[DockerRequiredTheory]` for transactions, uniqueness,
  concurrency and every new feature (one abstract scenario class, one sealed subclass per server)

## Context to load
- `.claude/rules/testing.md`
- `.claude/rules/architecture.md`
- Existing tests in the target project (fixtures, naming, helpers)

## Test matrix

### Write repository
- [ ] `FindAsync_WhenExists_ReturnsAggregate` / `FindAsync_WhenMissing_ReturnsNull`
- [ ] `AddAsync_ThenCommit_PersistsAggregate`
- [ ] `FindAsync_ThenMutate_ThenCommit_PersistsChange` (there is no `UpdateAsync`)
- [ ] `DeleteAsync_ThenCommit_RemovesAggregate`
- [ ] EF: `FindAsync_LoadsWholeAggregate` (children loaded, other aggregates not)

### Read repository
- [ ] `ListAsync_WithSpec_Filters`
- [ ] `ListPagedAsync_WithOrderingAndPagination_ReturnsPage`
- [ ] `ListPagedAsync_WithoutPagination_ReturnsDefaultPage`
- [ ] `ListAsync_AboveMaxPageSize_Throws`
- [ ] `AnyAsync_*`, `CountAsync_*`
- [ ] EF: `FindAsync_ReturnsTrackedInstanceAsIs` / `FindAsync_LoadsUntracked`

### Unit of Work
- [ ] `CommitAsync_WhenCancelled_ThrowsOperationCanceled`
- [ ] EF: `CommitAsync_OnUpdateFailure_ThrowsPersistenceException`
- [ ] EF: `DiscardChanges_ClearsPendingChanges`
- [ ] PostgreSQL: retry, join of an open transaction, rollback that keeps the original exception

## Pattern

```csharp
public sealed class OrderHandlerTests
{
    private readonly InMemoryRepository<Order, OrderId> _orders = new();
    private readonly InMemoryUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task FindAsync_WhenExists_ReturnsAggregate()
    {
        var order = Order.Place("SO-1", new Actor("user-1"), TimeProvider.System);
        await _orders.AddAsync(order);
        await _unitOfWork.CommitAsync();

        var found = await _orders.FindAsync(order.Id);

        found.ShouldNotBeNull();
        found.Id.ShouldBe(order.Id);
    }

    [Fact]
    public async Task CommitAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await _unitOfWork.CommitAsync(cts.Token));
    }
}
```

`InMemoryRepository` resolves the key from a public `Id` property of type `TId`, or from the
`idSelector` passed to its constructor; composite keys are not supported.

## Conventions
`sealed` test classes, `Method_Scenario_ExpectedResult`; test `.csproj` with
`GenerateDocumentationFile=false` and `NoWarn` `CS1591;CA1707`.
