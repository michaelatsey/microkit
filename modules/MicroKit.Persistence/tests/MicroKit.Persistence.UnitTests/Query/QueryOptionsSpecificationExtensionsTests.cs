using System.Linq.Expressions;

namespace MicroKit.Persistence.UnitTests.Query;

public sealed class QueryOptionsSpecificationExtensionsTests
{
    [Fact]
    public void WithSpec_WhenOptsIsNull_ThrowsArgumentNullException()
    {
        QueryOptions<TestAggregate> opts = null!;
        Should.Throw<ArgumentNullException>(() => opts.WithSpec(new MatchAllSpec()));
    }

    [Fact]
    public void WithSpec_WithConcreteSpec_SetsSpecification()
    {
        var spec = new MatchAllSpec();
        var opts = new QueryOptions<TestAggregate>().WithSpec(spec);

        opts.Specification.ShouldBeSameAs(spec);
        opts.Specification!.IsSatisfiedBy(new TestAggregate()).ShouldBeTrue();
    }

    [Fact]
    public void WithSpec_WithNullSpec_ClearsSpecification()
    {
        var opts = new QueryOptions<TestAggregate>(new MatchAllSpec()).WithSpec(null);
        opts.Specification.ShouldBeNull();
    }

    [Fact]
    public void WithSpec_ReplacesExistingSpecification()
    {
        var candidate = new TestAggregate { Active = false };
        var opts = new QueryOptions<TestAggregate>(new MatchAllSpec())
            .WithSpec(new ActiveOnlySpec());

        opts.Specification!.IsSatisfiedBy(candidate).ShouldBeFalse();
        opts.Specification.IsSatisfiedBy(new TestAggregate { Active = true }).ShouldBeTrue();
    }

    [Fact]
    public void WithSpec_PreservesAllOtherOptions()
    {
        Func<IQueryable<TestAggregate>, IQueryable<TestAggregate>> includes = q => q;
        Func<IQueryable<TestAggregate>, IOrderedQueryable<TestAggregate>> orderBy = q => q.OrderBy(a => a.Active);
        var pagination = new PaginationOptions(Page: 2, PageSize: 10);

        var original = new QueryOptions<TestAggregate>() with
        {
            Includes           = includes,
            OrderBy            = orderBy,
            Pagination         = pagination,
            AsNoTrackingEnabled = false,
            AsSplitQueryEnabled = true,
            IncludeDeleted      = true,
        };

        var updated = original.WithSpec(new MatchAllSpec());

        updated.Includes.ShouldBeSameAs(includes);
        updated.OrderBy.ShouldBeSameAs(orderBy);
        updated.Pagination.ShouldBe(pagination);
        updated.AsNoTrackingEnabled.ShouldBeFalse();
        updated.AsSplitQueryEnabled.ShouldBeTrue();
        updated.IncludeDeleted.ShouldBeTrue();
    }

    [Fact]
    public void WithSpec_DoesNotMutateOriginalOpts()
    {
        var originalSpec = new MatchAllSpec();
        var original = new QueryOptions<TestAggregate>(originalSpec);
        _ = original.WithSpec(new ActiveOnlySpec());

        original.Specification.ShouldBeSameAs(originalSpec);
    }

    // ---------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------

    private sealed class TestAggregate : IAggregateRoot
    {
        public bool Active { get; init; }
    }

    private sealed class MatchAllSpec : Specification<TestAggregate>
    {
        public override bool IsSatisfiedBy(TestAggregate candidate) => true;
        public override Expression<Func<TestAggregate, bool>> ToExpression() => _ => true;
    }

    private sealed class ActiveOnlySpec : Specification<TestAggregate>
    {
        public override bool IsSatisfiedBy(TestAggregate candidate) => candidate.Active;
        public override Expression<Func<TestAggregate, bool>> ToExpression() => a => a.Active;
    }
}
