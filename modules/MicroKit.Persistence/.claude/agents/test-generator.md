---
name: test-generator
description: Use this agent to generate test suites for MicroKit.Persistence repositories, specifications, and UoW patterns. Produces Shouldly + NSubstitute test files using InMemoryRepository from MicroKit.Persistence.Testing. 
tools: Read, Glob, Grep
model: sonnet
---

# Agent: Persistence Test Generator

## Stack (non-negotiable)
- **xUnit** — test framework
- **Shouldly** — all assertions (`result.ShouldBe(...)`, `result.ShouldNotBeNull()`)
- **FluentAssertions is banned** (commercial licence) — never reference it
- **NSubstitute** — mocks for external dependencies
- **MicroKit.Persistence.Testing** — `InMemoryRepository<T>` for repository isolation (`InMemoryRepository<TAggregate, TId>` from #196 phase 2)

## Context to load
- `.claude/rules/testing.md`
- `.claude-context/templates/test-repository-template.md`
- `.claude-context/standards/repository-contracts.md`

## Mandatory Test Matrix

### Per Repository
- [ ] `FindAsync_WhenExists_ReturnsAggregate`
- [ ] `FindAsync_WhenNotFound_ReturnsNull`
- [ ] `AddAsync_ThenUnitOfWorkCommit_PersistsAggregate`
- [ ] `FindAsync_ThenMutateThenUnitOfWorkCommit_PersistsChange` (`UpdateAsync` is removed by #196 D11, phase 2)
- [ ] `DeleteAsync_ThenUnitOfWorkCommit_RemovesAggregate`

### Per ReadRepository
- [ ] `ListAsync_WithSpec_FiltersCorrectly`
- [ ] `ListAsync_WithPagination_ReturnsCorrectPage`
- [ ] `AnyAsync_WhenMatches_ReturnsTrue`
- [ ] `CountAsync_ReturnsCorrectCount`
- [ ] `ListAsync_UsesAsNoTracking` (verify no tracking on InMemoryRepository)

### Per UnitOfWork / Transaction
- [ ] `CommitAsync_CommitsAllPendingChanges`
- [ ] `CommitAsync_WhenCancelled_ThrowsOperationCancelled`
- [ ] `ExecuteAsync_WhenOperationThrows_RollsBack` (PostgreSQL)
- [ ] `ExecuteAsync_InsideOpenTransaction_JoinsIt` (PostgreSQL — from #196 phase 1, D4)

## Test Pattern

```csharp
public sealed class UserRepositoryTests
{
    private readonly InMemoryRepository<User> _repo = new();
    private readonly InMemoryUnitOfWork _uow = new();

    [Fact]
    public async Task FindAsync_WhenExists_ReturnsUser()
    {
        var user = User.Create(UserId.New(), Email.From("test@example.com"));
        await _repo.AddAsync(user);
        await _uow.CommitAsync();

        var found = await _repo.FindAsync(user.Id);

        found.ShouldNotBeNull();
        found!.Id.ShouldBe(user.Id);
    }

    [Fact]
    public async Task FindAsync_WhenNotFound_ReturnsNull()
    {
        var found = await _repo.FindAsync(UserId.New());

        found.ShouldBeNull();
    }

    [Fact]
    public async Task CommitAsync_WhenCancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await _uow.CommitAsync(cts.Token));
    }
}
```

## Test Project Configuration
```xml
<GenerateDocumentationFile>false</GenerateDocumentationFile>
<NoWarn>CS1591;CA1707</NoWarn>
```
