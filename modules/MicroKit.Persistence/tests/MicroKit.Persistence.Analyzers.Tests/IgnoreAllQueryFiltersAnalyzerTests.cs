using MicroKit.Persistence.Analyzers.Tests.Stubs;

namespace MicroKit.Persistence.Analyzers.Tests;

/// <summary>MKP009 — the parameterless <c>IgnoreQueryFilters()</c> (#197 phase 6, ADR-014).</summary>
public sealed class IgnoreAllQueryFiltersAnalyzerTests
{
    private static Task Verify(string code, StubSet stubs = StubSet.All) =>
        QueryTest<IgnoreAllQueryFiltersAnalyzer>.RunAsync(code, stubs);

    // ---- Violations ----------------------------------------------------------------------------

    [Fact]
    public Task MKP009_OnADbSet_Reports() => Verify("""
        class Reads
        {
            Task<List<Order>> All(AppDbContext ctx) => ctx.Orders.{|MKP009:IgnoreQueryFilters()|}.ToListAsync();
        }
        """);

    [Fact]
    public Task MKP009_InTheStaticCallForm_Reports() => Verify("""
        class Reads
        {
            IQueryable<Order> All(AppDbContext ctx) => EntityFrameworkQueryableExtensions.{|MKP009:IgnoreQueryFilters(ctx.Orders)|};
        }
        """);

    [Fact]
    public Task MKP009_InsideTheIncludesOfQueryOptions_Reports() => Verify("""
        class Reads
        {
            QueryOptions<Order> Everything() => new QueryOptions<Order>().WithIncludes(q => q.{|MKP009:IgnoreQueryFilters()|});
        }
        """);

    [Fact]
    public Task MKP009_AsAMethodGroup_Reports() => Verify("""
        class Reads
        {
            Func<IQueryable<Order>, IQueryable<Order>> Unfiltered() => {|MKP009:EntityFrameworkQueryableExtensions.IgnoreQueryFilters<Order>|};
        }
        """);

    // ---- Valid code ----------------------------------------------------------------------------

    [Fact]
    public Task MKP009_TheNamedOverload_TheEvaluatorsShape_DoesNotReport() => Verify("""
        static class SoftDelete { public const string FilterName = "MicroKit.SoftDelete"; }

        class Evaluator
        {
            IQueryable<Order> WithDeleted(IQueryable<Order> query) => query.IgnoreQueryFilters([SoftDelete.FilterName]);
            IQueryable<Order> Tenantless(AppDbContext ctx) => ctx.Orders.IgnoreQueryFilters(new[] { "Tenant" });
        }
        """);

    [Fact]
    public Task MKP009_WithDeleted_DoesNotReport() => Verify("""
        class Reads
        {
            QueryOptions<Order> Archive() => new QueryOptions<Order>().WithDeleted();
        }
        """);

    [Fact]
    public Task MKP009_AMethodOfTheSameNameOnAnotherType_DoesNotReport() => Verify("""
        static class MyFilters
        {
            public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> source) => source;
        }

        class Reads
        {
            IQueryable<Order> All(IQueryable<Order> orders) => MyFilters.IgnoreQueryFilters(orders);
        }
        """);

    // ---- Edge cases ----------------------------------------------------------------------------

    [Fact]
    public Task MKP009_AnEmptyListOfNames_IgnoresNothing_DoesNotReport() => Verify("""
        class Reads
        {
            IQueryable<Order> All(AppDbContext ctx) => ctx.Orders.IgnoreQueryFilters(new string[0]);
        }
        """);

    [Fact]
    // nameof binds no method reference: the operation tree holds none to report.
    public Task MKP009_InsideNameOf_DoesNotReport() => Verify("""
        class Reads
        {
            const string Name = nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters);
        }
        """);

    [Fact]
    public Task MKP009_InsideAnExpressionTree_Reports() => Verify("""
        class Reads
        {
            Expression<Func<IQueryable<Order>, IQueryable<Order>>> Unfiltered = q => q.{|MKP009:IgnoreQueryFilters()|};
        }
        """);

    [Fact]
    public Task MKP009_WithoutEntityFrameworkCore_DoesNotReportOrFail() => QueryTest<IgnoreAllQueryFiltersAnalyzer>.RunAsync("""
        using System.Linq;

        static class Look
        {
            public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> source) => source;
        }

        class Reads
        {
            IQueryable<int> All(IQueryable<int> numbers) => numbers.IgnoreQueryFilters();
        }
        """, StubSet.None, usings: false);
}
