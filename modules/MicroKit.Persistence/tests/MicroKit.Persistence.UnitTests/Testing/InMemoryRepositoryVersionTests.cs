using MicroKit.Persistence.Abstractions;
using MicroKit.Persistence.Testing;

namespace MicroKit.Persistence.UnitTests.Testing;

/// <summary>
/// #197 phase 2: the in-memory double's version. It cannot see mutations, so it never bumps on its
/// own; it checks an expected version at the find, where EF Core checks it at the commit.
/// </summary>
public sealed class InMemoryRepositoryVersionTests
{
    private readonly InMemoryRepository<TestItem, TestItemId> _repo = new();

    [Fact]
    public async Task GetVersion_OfAStoredAggregate_IsOne()
    {
        var item = new TestItem("Alice");
        await _repo.AddAsync(item);

        _repo.GetVersion(item).ShouldBe(1);
    }

    [Fact]
    public async Task SetVersion_ChangesWhatGetVersionReads()
    {
        var item = new TestItem("Alice");
        await _repo.AddAsync(item);

        _repo.SetVersion(item, 7);

        _repo.GetVersion(item).ShouldBe(7);
    }

    [Fact]
    public async Task FindAsync_WithTheStoredVersion_ReturnsTheAggregate()
    {
        var item = new TestItem("Alice");
        await _repo.AddAsync(item);
        _repo.SetVersion(item, 3);

        (await _repo.FindAsync(item.Id, expectedVersion: 3)).ShouldBeSameAs(item);
    }

    [Fact]
    public async Task FindAsync_WithAStaleVersion_ThrowsAConcurrencyPersistenceException()
    {
        var item = new TestItem("Alice");
        await _repo.AddAsync(item);
        _repo.SetVersion(item, 3);

        var thrown = await Should.ThrowAsync<PersistenceException>(async () =>
            await _repo.FindAsync(item.Id, expectedVersion: 2));

        thrown.Message.ShouldContain("concurrency");
        thrown.InnerException.ShouldBeOfType<AggregateVersionConflictException>().Message.ShouldContain("version 3, not 2");
    }

    [Fact]
    public async Task FindAsync_WithAnExpectedVersion_WhenMissing_ReturnsNull()
    {
        (await _repo.FindAsync(new TestItemId(Guid.NewGuid()), expectedVersion: 1)).ShouldBeNull();
    }

    [Fact]
    public void GetVersion_OfAnAggregateNotStored_Throws()
    {
        Should.Throw<InvalidOperationException>(() => _repo.GetVersion(new TestItem("Bob")));
        Should.Throw<InvalidOperationException>(() => _repo.SetVersion(new TestItem("Bob"), 2));
    }

    [Fact]
    public async Task DeleteAsync_ForgetsTheVersion()
    {
        var item = new TestItem("Alice");
        await _repo.AddAsync(item);
        _repo.SetVersion(item, 5);
        await _repo.DeleteAsync(item);

        await _repo.AddAsync(item);

        _repo.GetVersion(item).ShouldBe(1);
    }
}
