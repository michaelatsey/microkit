namespace MicroKit.Persistence.UnitTests.Query;

/// <summary>#196 D6: a page is one-based and non-empty, and its offset cannot overflow.</summary>
public sealed class PaginationOptionsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Ctor_WhenPageIsBelowOne_Throws(int page) =>
        Should.Throw<ArgumentOutOfRangeException>(() => new PaginationOptions(page, 10));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Ctor_WhenPageSizeIsBelowOne_Throws(int pageSize) =>
        Should.Throw<ArgumentOutOfRangeException>(() => new PaginationOptions(1, pageSize));

    [Fact]
    public void Ctor_WhenTheOffsetWouldOverflow_Throws() =>
        Should.Throw<ArgumentOutOfRangeException>(() => new PaginationOptions(int.MaxValue, 2));

    [Fact]
    public void Skip_IsTheNumberOfItemsBeforeThePage() =>
        new PaginationOptions(3, 20).Skip.ShouldBe(40);

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    public void WithPagination_WithAnInvalidPage_Throws(int page, int pageSize) =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new QueryOptions<Testing.TestItem>().WithPagination(page, pageSize));
}

/// <summary>#196 D6: the limits a read repository enforces are themselves validated.</summary>
public sealed class PaginationLimitsTests
{
    [Theory]
    [InlineData(0, 500)]
    [InlineData(-1, 500)]
    [InlineData(50, 0)]
    [InlineData(600, 500)]
    public void Ctor_WithAnInvalidConfiguration_Throws(int defaultPageSize, int maxPageSize) =>
        Should.Throw<ArgumentOutOfRangeException>(() => new PaginationLimits(defaultPageSize, maxPageSize));

    [Fact]
    public void Default_IsAPageOfFiftyAndAMaximumOfFiveHundred()
    {
        PaginationLimits.Default.DefaultPageSize.ShouldBe(50);
        PaginationLimits.Default.MaxPageSize.ShouldBe(500);
    }

    [Fact]
    public void ForPagedQuery_WithoutPagination_ReturnsTheFirstDefaultPage() =>
        new PaginationLimits(20, 100).ForPagedQuery(null).ShouldBe(new PaginationOptions(1, 20));

    [Fact]
    public void ForPagedQuery_AtTheMaximum_ReturnsTheRequest()
    {
        var requested = new PaginationOptions(3, 100);
        new PaginationLimits(20, 100).ForPagedQuery(requested).ShouldBeSameAs(requested);
    }

    [Fact]
    public void ForPagedQuery_AboveTheMaximum_Throws() =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new PaginationLimits(20, 100).ForPagedQuery(new PaginationOptions(1, 101)));
}
