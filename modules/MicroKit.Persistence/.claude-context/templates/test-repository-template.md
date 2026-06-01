# Template: Repository Test Suite

Use this template for generating the mandatory test matrix for a new repository pair.

---

## Write Repository Tests

```csharp
namespace MicroKit.Persistence.UnitTests.Repositories;

public sealed class {Entity}RepositoryTests
{
    private readonly InMemoryRepository<{Entity}> _repo = new();
    private readonly InMemoryUnitOfWork _uow = new();

    [Fact]
    public async Task FindAsync_WhenExists_Returns{Entity}()
    {
        var entity = {Entity}.CreateTestInstance();
        await _repo.AddAsync(entity);
        await _uow.CommitAsync();

        var found = await _repo.FindAsync(entity.Id);

        found.ShouldNotBeNull();
        found!.Id.ShouldBe(entity.Id);
    }

    [Fact]
    public async Task FindAsync_WhenNotFound_ReturnsNull()
    {
        var found = await _repo.FindAsync({Entity}Id.New());

        found.ShouldBeNull();
    }

    [Fact]
    public async Task AddAsync_ThenCommit_Persists{Entity}()
    {
        var entity = {Entity}.CreateTestInstance();

        await _repo.AddAsync(entity);
        await _uow.CommitAsync();

        var found = await _repo.FindAsync(entity.Id);
        found.ShouldNotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_ThenCommit_Updates{Entity}()
    {
        var entity = {Entity}.CreateTestInstance();
        await _repo.AddAsync(entity);
        await _uow.CommitAsync();

        entity.Update{Property}({newValue});
        await _repo.UpdateAsync(entity);
        await _uow.CommitAsync();

        var updated = await _repo.FindAsync(entity.Id);
        updated!.{Property}.ShouldBe({newValue});
    }

    [Fact]
    public async Task DeleteAsync_ThenCommit_Removes{Entity}()
    {
        var entity = {Entity}.CreateTestInstance();
        await _repo.AddAsync(entity);
        await _uow.CommitAsync();

        await _repo.DeleteAsync(entity);
        await _uow.CommitAsync();

        var found = await _repo.FindAsync(entity.Id);
        found.ShouldBeNull();
    }

    [Fact]
    public async Task CommitAsync_WhenCancelled_ThrowsOperationCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await _uow.CommitAsync(cts.Token));
    }
}
```

---

## Read Repository Tests

```csharp
public sealed class {Entity}ReadRepositoryTests
{
    private readonly InMemoryRepository<{Entity}> _repo = new();

    [Fact]
    public async Task ListAsync_WithSpec_FiltersCorrectly()
    {
        var activeEntity = {Entity}.CreateActive();
        var inactiveEntity = {Entity}.CreateInactive();
        await _repo.AddAsync(activeEntity);
        await _repo.AddAsync(inactiveEntity);

        var opts = new QueryOptions<{Entity}>(new Active{Entity}Spec());
        var results = await _repo.ListAsync(opts);

        results.Count.ShouldBe(1);
        results.ShouldContain(e => e.Id == activeEntity.Id);
    }

    [Fact]
    public async Task ListAsync_WithPagination_ReturnsCorrectPage()
    {
        for (var i = 0; i < 25; i++)
            await _repo.AddAsync({Entity}.CreateTestInstance());

        var opts = new QueryOptions<{Entity}>()
            .WithPagination(page: 2, pageSize: 10);
        var results = await _repo.ListAsync(opts);

        results.Count.ShouldBe(10);
    }

    [Fact]
    public async Task AnyAsync_WhenMatches_ReturnsTrue()
    {
        var entity = {Entity}.CreateActive();
        await _repo.AddAsync(entity);

        var opts = new QueryOptions<{Entity}>(new Active{Entity}Spec());
        var any = await _repo.AnyAsync(opts);

        any.ShouldBeTrue();
    }

    [Fact]
    public async Task CountAsync_ReturnsCorrectCount()
    {
        await _repo.AddAsync({Entity}.CreateActive());
        await _repo.AddAsync({Entity}.CreateActive());
        await _repo.AddAsync({Entity}.CreateInactive());

        var opts = new QueryOptions<{Entity}>(new Active{Entity}Spec());
        var count = await _repo.CountAsync(opts);

        count.ShouldBe(2);
    }
}
```
