namespace MicroKit.Persistence.UnitTests.Query;

public sealed class QueryOptionsOrderingExtensionsTests
{
    // Helper — materialises the stored ordering delegate against an in-memory list.
    private static List<TestAggregate> ApplyOrder(
        QueryOptions<TestAggregate> opts,
        IEnumerable<TestAggregate> source)
    {
        var orderFn = opts.OrderBy;
        orderFn.ShouldNotBeNull();
        return orderFn(source.AsQueryable()).ToList();
    }

    // ---------------------------------------------------------------------------
    // Null guards
    // ---------------------------------------------------------------------------

    [Fact]
    public void WithOrderBy_WhenOptsIsNull_ThrowsArgumentNullException()
    {
        QueryOptions<TestAggregate> opts = null!;
        Should.Throw<ArgumentNullException>(() => opts.WithOrderBy(a => a.Name));
    }

    [Fact]
    public void WithOrderBy_WhenKeySelectorIsNull_ThrowsArgumentNullException()
    {
        var opts = new QueryOptions<TestAggregate>();
        Should.Throw<ArgumentNullException>(() =>
            opts.WithOrderBy((System.Linq.Expressions.Expression<Func<TestAggregate, string>>)null!));
    }

    [Fact]
    public void WithOrderByDescending_WhenOptsIsNull_ThrowsArgumentNullException()
    {
        QueryOptions<TestAggregate> opts = null!;
        Should.Throw<ArgumentNullException>(() => opts.WithOrderByDescending(a => a.Name));
    }

    [Fact]
    public void WithThenBy_WhenOptsIsNull_ThrowsArgumentNullException()
    {
        QueryOptions<TestAggregate> opts = null!;
        Should.Throw<ArgumentNullException>(() => opts.WithThenBy(a => a.Priority));
    }

    // ---------------------------------------------------------------------------
    // WithOrderBy / WithOrderByDescending correctness
    // ---------------------------------------------------------------------------

    [Fact]
    public void WithOrderBy_WithKeySelector_SortsAscending()
    {
        var opts = new QueryOptions<TestAggregate>().WithOrderBy(a => a.Name);
        var list = new List<TestAggregate>
        {
            new() { Name = "Charlie", Priority = 1 },
            new() { Name = "Alice",   Priority = 2 },
            new() { Name = "Bob",     Priority = 3 },
        };

        var result = ApplyOrder(opts, list);

        result[0].Name.ShouldBe("Alice");
        result[1].Name.ShouldBe("Bob");
        result[2].Name.ShouldBe("Charlie");
    }

    [Fact]
    public void WithOrderByDescending_WithKeySelector_SortsDescending()
    {
        var opts = new QueryOptions<TestAggregate>().WithOrderByDescending(a => a.Priority);
        var list = new List<TestAggregate>
        {
            new() { Name = "A", Priority = 1 },
            new() { Name = "B", Priority = 3 },
            new() { Name = "C", Priority = 2 },
        };

        var result = ApplyOrder(opts, list);

        result[0].Priority.ShouldBe(3);
        result[1].Priority.ShouldBe(2);
        result[2].Priority.ShouldBe(1);
    }

    [Fact]
    public void WithOrderBy_CalledTwice_SecondCallReplacesFirst()
    {
        var opts = new QueryOptions<TestAggregate>()
            .WithOrderBy(a => a.Priority)
            .WithOrderBy(a => a.Name);  // second call wins

        var list = new List<TestAggregate>
        {
            new() { Name = "Charlie", Priority = 1 },
            new() { Name = "Alice",   Priority = 3 },
            new() { Name = "Bob",     Priority = 2 },
        };

        var result = ApplyOrder(opts, list);

        result[0].Name.ShouldBe("Alice");
        result[1].Name.ShouldBe("Bob");
        result[2].Name.ShouldBe("Charlie");
    }

    // ---------------------------------------------------------------------------
    // WithThenBy / WithThenByDescending composition
    // ---------------------------------------------------------------------------

    [Fact]
    public void WithThenBy_WhenNoExistingOrder_BehavesAsOrderBy()
    {
        var opts = new QueryOptions<TestAggregate>().WithThenBy(a => a.Name);
        var list = new List<TestAggregate>
        {
            new() { Name = "Z", Priority = 1 },
            new() { Name = "A", Priority = 2 },
        };

        var result = ApplyOrder(opts, list);

        result[0].Name.ShouldBe("A");
        result[1].Name.ShouldBe("Z");
    }

    [Fact]
    public void WithThenBy_WhenOrderByExists_ComposesCorrectly()
    {
        var opts = new QueryOptions<TestAggregate>()
            .WithOrderBy(a => a.Priority)
            .WithThenBy(a => a.Name);

        var list = new List<TestAggregate>
        {
            new() { Name = "Charlie", Priority = 1 },
            new() { Name = "Alice",   Priority = 1 },
            new() { Name = "Bob",     Priority = 2 },
        };

        var result = ApplyOrder(opts, list);

        result[0].Name.ShouldBe("Alice");    // Priority=1, Name asc → Alice
        result[1].Name.ShouldBe("Charlie");  // Priority=1, Name asc → Charlie
        result[2].Name.ShouldBe("Bob");      // Priority=2
    }

    [Fact]
    public void WithThenByDescending_WhenNoExistingOrder_BehavesAsOrderByDescending()
    {
        var opts = new QueryOptions<TestAggregate>().WithThenByDescending(a => a.Priority);
        var list = new List<TestAggregate>
        {
            new() { Name = "A", Priority = 1 },
            new() { Name = "B", Priority = 3 },
            new() { Name = "C", Priority = 2 },
        };

        var result = ApplyOrder(opts, list);

        result[0].Priority.ShouldBe(3);
        result[1].Priority.ShouldBe(2);
        result[2].Priority.ShouldBe(1);
    }

    [Fact]
    public void WithThenByDescending_WhenOrderByExists_ComposesOnExistingDescendingPrimary()
    {
        var opts = new QueryOptions<TestAggregate>()
            .WithOrderByDescending(a => a.Priority)
            .WithThenByDescending(a => a.Name);

        var list = new List<TestAggregate>
        {
            new() { Name = "Alice",   Priority = 2 },
            new() { Name = "Charlie", Priority = 2 },
            new() { Name = "Bob",     Priority = 1 },
        };

        var result = ApplyOrder(opts, list);

        result[0].Name.ShouldBe("Charlie");  // Priority=2, Name desc → Charlie
        result[1].Name.ShouldBe("Alice");    // Priority=2, Name desc → Alice
        result[2].Name.ShouldBe("Bob");      // Priority=1
    }

    // ---------------------------------------------------------------------------
    // Three-level chain
    // ---------------------------------------------------------------------------

    [Fact]
    public void WithOrderBy_WithThenBy_WithThenByDescending_ThreeLevelChainSortsCorrectly()
    {
        var opts = new QueryOptions<TestAggregate>()
            .WithOrderBy(a => a.Priority)
            .WithThenBy(a => a.Name)
            .WithThenByDescending(a => a.Score);

        var list = new List<TestAggregate>
        {
            new() { Name = "Alice", Priority = 1, Score = 10 },
            new() { Name = "Alice", Priority = 1, Score = 20 },
            new() { Name = "Bob",   Priority = 1, Score = 99 },
            new() { Name = "Zed",   Priority = 2, Score = 50 },
        };

        var result = ApplyOrder(opts, list);

        // Priority=1, Name=Alice, Score desc → 20 before 10
        result[0].ShouldSatisfyAllConditions(
            r => r.Priority.ShouldBe(1),
            r => r.Name.ShouldBe("Alice"),
            r => r.Score.ShouldBe(20));
        result[1].ShouldSatisfyAllConditions(
            r => r.Name.ShouldBe("Alice"),
            r => r.Score.ShouldBe(10));
        result[2].Name.ShouldBe("Bob");
        result[3].Name.ShouldBe("Zed");
    }

    // ---------------------------------------------------------------------------
    // Immutability
    // ---------------------------------------------------------------------------

    [Fact]
    public void WithOrderBy_DoesNotMutateOriginalOpts()
    {
        var original = new QueryOptions<TestAggregate>();
        _ = original.WithOrderBy(a => a.Name);
        original.OrderBy.ShouldBeNull();
    }

    [Fact]
    public void WithThenBy_DoesNotMutateOriginalOpts()
    {
        var original = new QueryOptions<TestAggregate>().WithOrderBy(a => a.Priority);
        var capturedOrderBy = original.OrderBy;
        _ = original.WithThenBy(a => a.Name);
        original.OrderBy.ShouldBeSameAs(capturedOrderBy);
    }

    // ---------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------

    private sealed class TestAggregate : IAggregateRoot
    {
        public string Name     { get; init; } = "";
        public int    Priority { get; init; }
        public int    Score    { get; init; }
    }
}
