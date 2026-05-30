# Rule: Testing — MicroKit.Persistence

## Test Project Responsibilities

| Project | Tests |
|---------|-------|
| `UnitTests` | Repository logic, QueryOptions, specifications — isolated via InMemoryRepository, no real DB |
| `IntegrationTests` | EF Core + real DB (SQLite in-memory or Testcontainers), full UoW cycle |
| `ArchitectureTests` | Dependency rules via NetArchTest, analyzer rule checks |
| `PerformanceTests` | Query overhead, allocation regression via BenchmarkDotNet |

## Library Choices

- **xUnit** — test framework
- **Shouldly** — all assertions — see root `.claude/rules/testing-libraries.md`
- **FluentAssertions is banned** — any `.Should()` call blocks the build
- **NSubstitute** — all mocks and stubs
- **MicroKit.Persistence.Testing** — `InMemoryRepository<T>`, `InMemoryUnitOfWork`
- **NetArchTest.Rules** — architecture tests only
- **Testcontainers** — integration tests against real PostgreSQL/SQL Server (opt-in, slow suite)

## Isolation Principle

Unit tests must not require a real database, EF Core context, or DI container:

```csharp
// ✅ Pure unit test using InMemoryRepository
var repo = new InMemoryRepository<User>();
var uow = new InMemoryUnitOfWork();

await repo.AddAsync(user);
await uow.CommitAsync();

var found = await repo.FindAsync(user.Id);
found.ShouldNotBeNull();
```

Integration tests use an in-memory SQLite DbContext or Testcontainers for provider-specific tests.

## xUnit Conventions

- Test classes: `sealed` — no inheritance
- Method naming: `Method_Scenario_ExpectedResult`
- `[Fact]` for deterministic tests, `[Theory]` + `[InlineData]` for parameterized
- `[Collection]` for sharing database fixtures across integration tests

## Mandatory Cases

### Per Repository
- [ ] `FindAsync_WhenExists_ReturnsAggregate`
- [ ] `FindAsync_WhenNotFound_ReturnsNull`
- [ ] `AddAsync_ThenCommit_PersistsAggregate`
- [ ] `UpdateAsync_ThenCommit_UpdatesAggregate`
- [ ] `DeleteAsync_ThenCommit_RemovesAggregate`
- [ ] `CommitAsync_WhenCancelled_ThrowsOperationCancelled`

### Per ReadRepository
- [ ] `ListAsync_WithSpec_FiltersCorrectly`
- [ ] `ListAsync_WithPagination_ReturnsCorrectPage`
- [ ] `AnyAsync_WhenMatches_ReturnsTrue`
- [ ] `AnyAsync_WhenNoMatch_ReturnsFalse`
- [ ] `CountAsync_ReturnsCorrectCount`

### Per Specification
- [ ] `Criteria_WhenSatisfied_MatchesAggregate`
- [ ] `Criteria_WhenNotSatisfied_DoesNotMatch`
- [ ] `QueryOptions_WithSpec_AppliesCorrectFilter`

## Test Project .csproj

```xml
<GenerateDocumentationFile>false</GenerateDocumentationFile>
<NoWarn>CS1591;CA1707</NoWarn>
```

## Detecting Violations

```bash
# FluentAssertions must never appear
grep -rn 'FluentAssertions\|\.Should()\.' modules/MicroKit.Persistence/tests/ --include="*.cs"
```
