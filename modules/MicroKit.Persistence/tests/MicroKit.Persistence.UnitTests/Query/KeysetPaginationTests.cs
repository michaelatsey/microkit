namespace MicroKit.Persistence.UnitTests.Query;

/// <summary>
/// #197 phase 5: a keyset pagination is built from non-nullable sort keys, owns the ordering and the page of the
/// options it is set on, and is bounded by <see cref="PaginationLimits"/>.
/// </summary>
public sealed class KeysetPaginationTests
{
    [Fact]
    public void TheKeys_AreKeptInOrder_WithTheirDirections()
    {
        var keyset = KeysetPagination<Order>.OrderByDescending(o => o.PlacedAt).ThenBy(o => o.Number).ThenByDescending(o => o.Lines);

        keyset.Keys.Select(k => (k.Selector.ToString(), k.Descending))
            .ShouldBe([("o => o.PlacedAt", true), ("o => o.Number", false), ("o => o.Lines", true)]);
        keyset.PageSize.ShouldBeNull();
        keyset.After.ShouldBeNull();
    }

    [Fact]
    public void ANullableSortKey_IsRefused_WhenTheKeysetIsBuilt()
    {
        Should.Throw<ArgumentException>(() => KeysetPagination<Order>.OrderBy(o => o.ShippedAt)).Message.ShouldContain("nullable");
        Should.Throw<ArgumentException>(() => KeysetPagination<Order>.OrderBy(o => o.Number).ThenByDescending(o => o.ShippedAt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void APageSizeBelowOne_IsRefused(int size) =>
        Should.Throw<ArgumentOutOfRangeException>(() => KeysetPagination<Order>.OrderBy(o => o.Number) with { PageSize = size });

    [Fact]
    public void WithKeyset_SetsAndClearsTheKeyset()
    {
        var keyset = KeysetPagination<Order>.OrderBy(o => o.Number);

        var opts = new QueryOptions<Order>().WithKeyset(keyset);

        opts.Keyset.ShouldBeSameAs(keyset);
        opts.WithKeyset(null).Keyset.ShouldBeNull();
    }

    [Fact]
    public void Require_ReturnsTheKeyset_AndRefusesOptionsWithoutOne()
    {
        var keyset = KeysetPagination<Order>.OrderBy(o => o.Number);

        KeysetRules.Require(new QueryOptions<Order>().WithKeyset(keyset)).ShouldBeSameAs(keyset);
        Should.Throw<ArgumentException>(() => KeysetRules.Require(new QueryOptions<Order>())).Message.ShouldContain("WithKeyset");
    }

    [Fact]
    public void AKeyset_OwnsTheOrderingAndThePage()
    {
        var opts = new QueryOptions<Order>().WithKeyset(KeysetPagination<Order>.OrderBy(o => o.Number));

        Should.Throw<ArgumentException>(() => KeysetRules.Require(opts.WithOrdering(q => q.OrderBy(o => o.Number))))
            .Message.ShouldContain("OrderBy");
        Should.Throw<ArgumentException>(() => KeysetRules.Require(opts.WithPagination(1, 10))).Message.ShouldContain("Pagination");
        Should.Throw<ArgumentException>(() => KeysetRules.EnsureNone(opts, "ListAsync")).Message.ShouldContain("ListKeysetAsync");
        Should.NotThrow(() => KeysetRules.EnsureNone(new QueryOptions<Order>(), "ListAsync"));
    }

    [Fact]
    public void TheKeysetPageSize_DefaultsToTheDefaultPage_AndIsBoundedByTheMaximum()
    {
        var limits = new PaginationLimits(defaultPageSize: 20, maxPageSize: 100);

        limits.ForKeysetQuery(null).ShouldBe(20);
        limits.ForKeysetQuery(100).ShouldBe(100);
        Should.Throw<ArgumentOutOfRangeException>(() => limits.ForKeysetQuery(101));
    }

    [Fact]
    public void AKeysetPage_HasANextPage_OnlyWithACursor()
    {
        new KeysetPage<int>([1], 1, Next: null).HasNextPage.ShouldBeFalse();
        new KeysetPage<int>([1], 1, KeysetCursor.Create("Order", [("Id", false)], [1])).HasNextPage.ShouldBeTrue();
    }

    private sealed class Order : IAggregateRoot
    {
        public string Number { get; init; } = "";

        public DateTimeOffset PlacedAt { get; init; }

        public DateTimeOffset? ShippedAt { get; init; }

        public int Lines { get; init; }
    }
}
