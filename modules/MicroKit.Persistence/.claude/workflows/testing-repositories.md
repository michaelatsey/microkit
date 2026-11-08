# Workflow: Testing Repositories

Commands: `.claude/skills/testing/SKILL.md`. Conventions and cases: `.claude/rules/testing.md`.

## Pick the layer

| What is tested | Where | With |
|----------------|-------|------|
| A handler's logic | UnitTests (or the consumer's) | `InMemoryRepository<TAggregate, TId>`, `InMemoryReadRepository<T>`, `InMemoryUnitOfWork` |
| QueryOptions, pagination, the doubles | UnitTests | plain xUnit + Shouldly |
| EF Core mapping, aggregate load, read tracking, pagination | IntegrationTests/EntityFrameworkCore | SQLite in-memory |
| Transactions, retries, uniqueness, concurrency | IntegrationTests/PostgreSql | `PostgreSqlFixture`, `[DockerRequiredFact]` |
| Provider registration | IntegrationTests/Providers | service-collection assertions |
| Layering, placement, sealing, IL calls | ArchitectureTests | NetArchTest, Mono.Cecil |
| MKP diagnostics | Analyzers.Tests | `CSharpAnalyzerTest`, `PersistenceStubs.All` |

## Unit test with the doubles

```csharp
var orders = new InMemoryRepository<Order, OrderId>();
var unitOfWork = new InMemoryUnitOfWork();

await orders.AddAsync(order);
await unitOfWork.CommitAsync();

unitOfWork.CommitCount.ShouldBe(1);
orders.All.Single().ShouldBe(order);
```

The doubles store on `AddAsync` (no staging), ignore `Includes`, and reject composite keys.

## Integration test

One SQLite connection per test (and per `Task.Run`), schema created by `EnsureCreated`; a
PostgreSQL test runs against the server `PostgreSqlFixture` starts once for the
`PostgreSqlSuite` collection, and skips without Docker.

## Generate

`test-generator` writes the matrix for a repository pair.

## Detect violations

```bash
grep -rn 'FluentAssertions' modules/MicroKit.Persistence --include="*.cs" --include="*.csproj"
dotnet build modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release   # MKP diagnostics
```
