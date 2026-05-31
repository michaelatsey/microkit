using System.Linq.Expressions;
using MicroKit.Persistence.Testing;

namespace MicroKit.Persistence.UnitTests.Testing;

// ---------------------------------------------------------------------------
// InMemoryRepository — write operations
// ---------------------------------------------------------------------------

public sealed class InMemoryRepositoryWriteTests
{
    private readonly InMemoryRepository<TestItem> _repo = new();

    [Fact]
    public async Task AddAsync_PersistsImmediately()
    {
        var item = new TestItem("Alice");

        await _repo.AddAsync(item);

        _repo.All.Count.ShouldBe(1);
    }

    [Fact]
    public async Task FindById_WhenExists_ReturnsAggregate()
    {
        var item = new TestItem("Alice");
        await _repo.AddAsync(item);

        var found = await _repo.FindById(item.Id);

        found.ShouldNotBeNull();
        found!.Id.ShouldBe(item.Id);
    }

    [Fact]
    public async Task FindById_WhenNotFound_ReturnsNull()
    {
        var found = await _repo.FindById(Guid.NewGuid());

        found.ShouldBeNull();
    }

    [Fact]
    public async Task FindAsync_WithKeyValuesArray_ReturnsAggregate()
    {
        var item = new TestItem("Alice");
        await _repo.AddAsync(item);

        var found = await _repo.FindAsync([item.Id]);

        found.ShouldNotBeNull();
        found!.Id.ShouldBe(item.Id);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesAggregate()
    {
        var item = new TestItem("Alice");
        await _repo.AddAsync(item);

        item.Rename("Alice Updated");
        await _repo.UpdateAsync(item);

        var found = await _repo.FindById(item.Id);
        found!.Name.ShouldBe("Alice Updated");
    }

    [Fact]
    public async Task DeleteAsync_RemovesAggregate()
    {
        var item = new TestItem("Alice");
        await _repo.AddAsync(item);

        await _repo.DeleteAsync(item);

        var found = await _repo.FindById(item.Id);
        found.ShouldBeNull();
    }

    [Fact]
    public async Task FindAsync_WithCompositePkArray_ThrowsNotSupported()
    {
        await Should.ThrowAsync<NotSupportedException>(
            async () => await _repo.FindAsync([Guid.NewGuid(), Guid.NewGuid()]));
    }

    [Fact]
    public async Task CommitAsync_WhenCancelled_ThrowsOperationCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await _repo.CommitAsync(cts.Token));
    }

    [Fact]
    public async Task CommitAsync_IsNoOp()
    {
        var item = new TestItem("Alice");
        await _repo.AddAsync(item);

        await _repo.CommitAsync();

        // Writes are immediate — CommitAsync is a no-op; state is unchanged.
        _repo.All.Count.ShouldBe(1);
    }
}

// ---------------------------------------------------------------------------
// InMemoryRepository — read operations
// ---------------------------------------------------------------------------

public sealed class InMemoryRepositoryReadTests
{
    private readonly InMemoryRepository<TestItem> _repo = new();

    [Fact]
    public async Task ListAsync_WithNullSpec_ReturnsAll()
    {
        await _repo.AddAsync(new TestItem("A"));
        await _repo.AddAsync(new TestItem("B"));

        var opts = new QueryOptions<TestItem>();
        var results = await _repo.ListAsync(opts);

        results.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ListAsync_WithSpec_FiltersViaSatisfiedBy()
    {
        await _repo.AddAsync(new TestItem("Alice"));
        await _repo.AddAsync(new TestItem("Bob"));

        var opts = new QueryOptions<TestItem>(new NameContainsSpec("Alice"));
        var results = await _repo.ListAsync(opts);

        results.Count.ShouldBe(1);
        results.ShouldContain(i => i.Name == "Alice");
    }

    [Fact]
    public async Task ListAsync_WithPagination_ReturnsCorrectPage()
    {
        for (var i = 0; i < 15; i++)
            await _repo.AddAsync(new TestItem($"Item{i:D2}"));

        var opts = new QueryOptions<TestItem>().WithPagination(page: 2, pageSize: 5);
        var results = await _repo.ListAsync(opts);

        results.Count.ShouldBe(5);
    }

    [Fact]
    public async Task ListAsync_WithOrderBy_SortsResults()
    {
        await _repo.AddAsync(new TestItem("Charlie"));
        await _repo.AddAsync(new TestItem("Alice"));
        await _repo.AddAsync(new TestItem("Bob"));

        var opts = new QueryOptions<TestItem>().WithOrderBy(i => i.Name);
        var results = await _repo.ListAsync(opts);

        results[0].Name.ShouldBe("Alice");
        results[1].Name.ShouldBe("Bob");
        results[2].Name.ShouldBe("Charlie");
    }

    [Fact]
    public async Task ListPagedAsync_ReturnsCorrectTotalCountAndPage()
    {
        for (var i = 0; i < 25; i++)
            await _repo.AddAsync(new TestItem($"Item{i:D2}"));

        var opts = new QueryOptions<TestItem>().WithPagination(page: 2, pageSize: 10);
        var result = await _repo.ListPagedAsync(opts);

        result.TotalCount.ShouldBe(25);
        result.Page.ShouldBe(2);
        result.PageSize.ShouldBe(10);
        result.Items.Count.ShouldBe(10);
    }

    [Fact]
    public async Task ListPagedAsync_WhenEmpty_ReturnsEmptyResult()
    {
        var opts = new QueryOptions<TestItem>().WithPagination(page: 1, pageSize: 10);
        var result = await _repo.ListPagedAsync(opts);

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task AnyAsync_WhenMatches_ReturnsTrue()
    {
        await _repo.AddAsync(new TestItem("Alice"));

        var opts = new QueryOptions<TestItem>(new NameContainsSpec("Alice"));
        var any = await _repo.AnyAsync(opts);

        any.ShouldBeTrue();
    }

    [Fact]
    public async Task AnyAsync_WhenNoMatch_ReturnsFalse()
    {
        await _repo.AddAsync(new TestItem("Alice"));

        var opts = new QueryOptions<TestItem>(new NameContainsSpec("Bob"));
        var any = await _repo.AnyAsync(opts);

        any.ShouldBeFalse();
    }

    [Fact]
    public async Task CountAsync_ReturnsCorrectCount()
    {
        await _repo.AddAsync(new TestItem("Alice"));
        await _repo.AddAsync(new TestItem("Alice2"));
        await _repo.AddAsync(new TestItem("Bob"));

        var opts = new QueryOptions<TestItem>(new NameContainsSpec("Alice"));
        var count = await _repo.CountAsync(opts);

        count.ShouldBe(2);
    }
}

// ---------------------------------------------------------------------------
// InMemoryReadRepository — seeded read-only double
// ---------------------------------------------------------------------------

public sealed class InMemoryReadRepositoryTests
{
    [Fact]
    public async Task FindAsync_WhenExists_ReturnsAggregate()
    {
        var item = new TestItem("Alice");
        var repo = new InMemoryReadRepository<TestItem>([item]);

        var found = await repo.FindAsync([item.Id]);

        found.ShouldNotBeNull();
        found!.Id.ShouldBe(item.Id);
    }

    [Fact]
    public async Task FindAsync_WhenNotFound_ReturnsNull()
    {
        var repo = new InMemoryReadRepository<TestItem>([new TestItem("Alice")]);

        var found = await repo.FindAsync([Guid.NewGuid()]);

        found.ShouldBeNull();
    }

    [Fact]
    public async Task ListAsync_WithSpec_FiltersCorrectly()
    {
        var items = new[]
        {
            new TestItem("Alice"),
            new TestItem("Bob"),
            new TestItem("Alice2"),
        };
        var repo = new InMemoryReadRepository<TestItem>(items);

        var opts = new QueryOptions<TestItem>(new NameContainsSpec("Alice"));
        var results = await repo.ListAsync(opts);

        results.Count.ShouldBe(2);
        results.ShouldAllBe(i => i.Name.Contains("Alice", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CountAsync_ReturnsCorrectCount()
    {
        var items = Enumerable.Range(1, 7).Select(i => new TestItem($"Item{i}"));
        var repo = new InMemoryReadRepository<TestItem>(items);

        var count = await repo.CountAsync(new QueryOptions<TestItem>());

        count.ShouldBe(7);
    }
}

// ---------------------------------------------------------------------------
// Test helpers — scoped to this file
// ---------------------------------------------------------------------------

// A minimal IAggregateRoot with a conventional Guid Id property.
internal sealed class TestItem : IAggregateRoot
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; private set; }

    public TestItem(string name) => Name = name;

    public void Rename(string newName) => Name = newName;
}

// Spec that checks whether Name contains a given search term.
internal sealed class NameContainsSpec : Specification<TestItem>
{
    private readonly string _term;
    public NameContainsSpec(string term) => _term = term;

    public override bool IsSatisfiedBy(TestItem candidate) =>
        candidate.Name.Contains(_term, StringComparison.Ordinal);

    public override Expression<Func<TestItem, bool>> ToExpression() =>
        item => item.Name.Contains(_term);
}
