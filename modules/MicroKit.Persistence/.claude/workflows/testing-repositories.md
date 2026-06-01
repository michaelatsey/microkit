# Workflow: Testing Repositories

Step-by-step guide for writing and running repository tests in MicroKit.Persistence.

## When to Use

When implementing a new repository, modifying an existing one, or debugging a failing test.

## Test Layers

| Layer | When | Tools |
|-------|------|-------|
| Unit | Always | `InMemoryRepository<T>`, `InMemoryUnitOfWork` |
| Integration | EF Core-specific behavior | SQLite in-memory + Testcontainers |
| Architecture | Dependency rules | NetArchTest |
| Performance | Allocation regression | BenchmarkDotNet |

## Unit Tests (Fast — No DB)

```bash
dotnet test modules/MicroKit.Persistence/tests/MicroKit.Persistence.UnitTests/ --no-build
```

Use `InMemoryRepository<T>` from `MicroKit.Persistence.Testing`:
```csharp
var repo = new InMemoryRepository<User>();
await repo.AddAsync(user);
// ...
```

## Integration Tests (EF Core + Real DB Logic)

```bash
# SQLite in-memory (fast, no container)
dotnet test modules/MicroKit.Persistence/tests/MicroKit.Persistence.IntegrationTests/ \
  --no-build --filter "Category=SQLite"

# Testcontainers PostgreSQL (requires Docker)
dotnet test modules/MicroKit.Persistence/tests/MicroKit.Persistence.IntegrationTests/ \
  --no-build --filter "Category=PostgreSQL"
```

## Architecture Tests

```bash
dotnet test modules/MicroKit.Persistence/tests/MicroKit.Persistence.ArchitectureTests/ --no-build
```

Checks:
- `IReadRepository` implementations have no mutation methods
- `DbContext` not injected directly into handlers
- Specification classes contain no Include or pagination
- EF Core types absent from Abstractions namespace

## Generate Tests

```
/new-repository-tests <AggregateName>
```

The `test-generator` agent produces the mandatory test matrix for the repository pair.

## Filter Tests

```bash
dotnet test --filter "ClassName=UserRepositoryTests"
dotnet test --filter "Name~FindAsync"
dotnet test --filter "Category=UnitTest"
```

## Detecting Violations

```bash
# FluentAssertions must never appear
grep -rn 'FluentAssertions\|\.Should()\.' modules/MicroKit.Persistence/tests/ --include="*.cs"

# Check analyzer violations compile-time
dotnet build modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Debug
```
