---
name: test-generator
description: Use this agent to generate test suites for MicroKit.Persistence repositories, specifications, and UoW patterns. Produces Shouldly + NSubstitute test files using InMemoryRepository from MicroKit.Persistence.Testing. Invoked by /new-repository-tests.
tools: Read, Glob, Grep
model: sonnet
---

# Agent: Persistence Test Generator

## Stack (non-negotiable)
- **xUnit** — test framework
- **Shouldly** — all assertions (`result.ShouldBe(...)`, `result.ShouldNotBeNull()`)
- **FluentAssertions is banned** — any `.Should()` triggers a build error
- **NSubstitute** — mocks for external dependencies
- **MicroKit.Persistence.Testing** — `InMemoryRepository<T>` for repository isolation

## Context to load
- `.claude/rules/testing.md`
- `.claude-context/templates/test-repository-template.md`
- `.claude-context/standards/repository-contracts.md`

## Mandatory Test Matrix

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
- [ ] `CountAsync_ReturnsCorrectCount`
- [ ] `ListAsync_UsesAsNoTracking` (verify no tracking on InMemoryRepository)

### Per UnitOfWork / Transaction
- [ ] `CommitAsync_CommitsAllPendingChanges`
- [ ] `BeginTransactionAsync_ThenRollback_RevertsPendingChanges`
- [ ] `NestedCommit_IdempotentWithinTransaction`

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
