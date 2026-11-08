using MicroKit.Persistence.Testing;

namespace MicroKit.Persistence.UnitTests.Testing;

/// <summary>
/// #196 D6 in the in-memory doubles, which mirror the EF Core read repository: a request without
/// pagination gets a bounded default page, and a page above the maximum size is rejected.
/// </summary>
public sealed class InMemoryPaginationTests
{
    [Fact]
    public async Task ListPagedAsync_WithoutPagination_ReturnsTheBoundedDefaultPage()
    {
        var repo = await SeedAsync(new InMemoryRepository<TestItem, TestItemId>(), 60);

        var result = await repo.ListPagedAsync(new QueryOptions<TestItem>());

        result.TotalCount.ShouldBe(60);
        result.Page.ShouldBe(1);
        result.PageSize.ShouldBe(50, "the default page size of the default limits");
        result.Items.Count.ShouldBe(50, "an unpaginated paged query never loads the whole table");
    }

    [Fact]
    public async Task ListPagedAsync_WhenEmptyWithoutPagination_ReportsTheDefaultPageSize()
    {
        var result = await new InMemoryRepository<TestItem, TestItemId>().ListPagedAsync(new QueryOptions<TestItem>());

        result.Items.ShouldBeEmpty();
        result.PageSize.ShouldBe(50);
    }

    [Fact]
    public async Task ListPagedAsync_WhenThePageSizeExceedsTheMaximum_Throws()
    {
        var repo = await SeedAsync(new InMemoryRepository<TestItem, TestItemId>(), 3);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
            await repo.ListPagedAsync(new QueryOptions<TestItem>().WithPagination(page: 1, pageSize: 501)));
    }

    [Fact]
    public async Task ListAsync_WhenThePageSizeExceedsTheMaximum_Throws()
    {
        var repo = await SeedAsync(new InMemoryRepository<TestItem, TestItemId>(), 3);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
            await repo.ListAsync(new QueryOptions<TestItem>().WithPagination(page: 1, pageSize: 501)));
    }

    [Fact]
    public async Task ReadRepository_WithoutPagination_ReturnsTheBoundedDefaultPage()
    {
        var items = Enumerable.Range(0, 60).Select(i => new TestItem($"Item{i:D2}")).ToList();
        var repo = new InMemoryReadRepository<TestItem>(items);

        var result = await repo.ListPagedAsync(new QueryOptions<TestItem>());

        result.Items.Count.ShouldBe(50);
        result.TotalCount.ShouldBe(60);
    }

    private static async Task<InMemoryRepository<TestItem, TestItemId>> SeedAsync(InMemoryRepository<TestItem, TestItemId> repo, int count)
    {
        for (var i = 0; i < count; i++)
            await repo.AddAsync(new TestItem($"Item{i:D2}"));
        return repo;
    }
}

public sealed class InMemoryConfiguredPaginationTests
{
    [Fact]
    public async Task ConfiguredLimits_ApplyToTheDefaultPageAndTheMaximum()
    {
        var repo = new InMemoryRepository<TestItem, TestItemId>(new PaginationLimits(defaultPageSize: 3, maxPageSize: 5));
        for (var i = 0; i < 10; i++)
            await repo.AddAsync(new TestItem($"Item{i:D2}"));

        (await repo.ListPagedAsync(new QueryOptions<TestItem>())).Items.Count.ShouldBe(3);
        (await repo.ListPagedAsync(new QueryOptions<TestItem>().WithPagination(1, 5))).Items.Count.ShouldBe(5);
        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
            await repo.ListPagedAsync(new QueryOptions<TestItem>().WithPagination(1, 6)));
    }
}

/// <summary>The double fails where the EF Core repository fails, instead of silently diverging.</summary>
public sealed class InMemoryRepositoryParityTests
{
    [Fact]
    public void Ctor_WhenTheIdPropertyIsNotOfTheIdType_Throws() =>
        Should.Throw<InvalidOperationException>(() => new InMemoryRepository<GuidKeyedItem, TestItemId>())
            .Message.ShouldContain("GuidKeyedItem");

    [Fact]
    public async Task AddAsync_WhenAnAggregateWithTheSameIdIsStored_Throws()
    {
        var repo = new InMemoryRepository<TestItem, TestItemId>();
        var item = new TestItem("Alice");
        await repo.AddAsync(item);

        await Should.ThrowAsync<InvalidOperationException>(async () => await repo.AddAsync(item));
    }

    internal sealed class GuidKeyedItem : IAggregateRoot
    {
        public Guid Id { get; } = Guid.NewGuid();
    }
}
