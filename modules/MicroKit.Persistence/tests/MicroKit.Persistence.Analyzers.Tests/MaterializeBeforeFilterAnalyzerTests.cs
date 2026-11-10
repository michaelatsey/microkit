using MicroKit.Persistence.Analyzers.Tests.Stubs;

namespace MicroKit.Persistence.Analyzers.Tests;

/// <summary>
/// MKP008 — a whole query materialized, then filtered in memory by a predicate that plainly translates
/// (#197 phase 6, ADR-014).
/// </summary>
public sealed class MaterializeBeforeFilterAnalyzerTests
{
    private static Task Verify(string code, StubSet stubs = StubSet.All) =>
        QueryTest<MaterializeBeforeFilterAnalyzer>.RunAsync(code, stubs);

    // ---- Violations ----------------------------------------------------------------------------

    [Fact]
    public Task MKP008_ToListThenWhere_Reports() => Verify("""
        class Reads
        {
            List<Order> Large(AppDbContext ctx, decimal min) =>
                ctx.Orders.{|MKP008:ToList()|}.Where(o => o.Total > min).ToList();
        }
        """);

    [Fact]
    public Task MKP008_ToArrayThenFirst_AndToListThenCount_Report() => Verify("""
        class Reads
        {
            Order First(AppDbContext ctx, string number) =>
                ctx.Orders.AsNoTracking().OrderBy(o => o.Number).{|MKP008:ToArray()|}.First(o => o.Number == number);

            int Paid(AppDbContext ctx) => ctx.Set<Order>().{|MKP008:ToList()|}.Count(o => o.Status == OrderStatus.Paid);
        }
        """);

    [Fact]
    public Task MKP008_AnAwaitedToListAsyncThenWhere_Reports() => Verify("""
        class Reads
        {
            async Task<List<Order>> Open(AppDbContext ctx, OrderStatus status, CancellationToken ct) =>
                (await ctx.Orders.Where(o => o.Priority > 1).{|MKP008:ToListAsync(ct)|}.ConfigureAwait(false))
                    .Where(o => o.Status == status && o.Number != "")
                    .ToList();

            async Task<bool> Any(AppDbContext ctx) =>
                (await ctx.Orders.{|MKP008:ToArrayAsync()|}).Any(o => o.Priority >= 3 || !(o.Number == null));
        }
        """);

    [Fact]
    public Task MKP008_ADateTimeOffsetComparison_WithACapturedCall_Reports() => Verify("""
        class Reads
        {
            List<Order> Due(AppDbContext ctx, IClock clock) =>
                ctx.Orders.{|MKP008:ToList()|}.Where(o => o.PlacedAt < clock.GetUtcNow() && (o.ShippedAt ?? o.PlacedAt) > DateTimeOffset.MinValue).ToList();
        }
        """);

    [Fact]
    public Task MKP008_StringAndDecimalOperators_AndContains_Report() => Verify("""
        class Reads
        {
            List<Order> Pick(AppDbContext ctx, string prefix, OrderStatus[] statuses, List<int> priorities) =>
                ctx.Orders.{|MKP008:ToList()|}
                    .Where(o => o.Number == prefix + "-1" && o.Total < 10m && o.Number.StartsWith(prefix) && statuses.Contains(o.Status) && priorities.Contains(o.Priority))
                    .ToList();
        }
        """);

    [Fact]
    public Task MKP008_ThroughTheIncludesAndHints_Reports() => Verify("""
        class Reads
        {
            List<Order> Large(AppDbContext ctx) =>
                ctx.Orders.Include(o => o.Lines).ThenInclude(l => l.Taxes).IgnoreQueryFilters(new[] { "Tenant" })
                    .TagWith("large").AsSplitQuery().AsTracking().{|MKP008:ToList()|}
                    .Where(o => o.Priority > 1).ToList();
        }
        """);

    [Fact]
    public Task MKP008_LastWithAnOrdering_Reports() => Verify("""
        class Reads
        {
            Order Latest(AppDbContext ctx) => ctx.Orders.OrderBy(o => o.PlacedAt).{|MKP008:ToList()|}.Last(o => o.Priority > 0);
        }
        """);

    // ---- Valid code ----------------------------------------------------------------------------

    [Fact]
    public Task MKP008_TheFilterBeforeToList_DoesNotReport() => Verify("""
        class Reads
        {
            List<Order> Large(AppDbContext ctx, decimal min) => ctx.Orders.Where(o => o.Total > min).ToList();
        }
        """);

    [Fact]
    public Task MKP008_AsEnumerable_TheExplicitClientEvaluation_DoesNotReport() => Verify("""
        class Reads
        {
            List<Order> Large(AppDbContext ctx, decimal min) => ctx.Orders.AsEnumerable().Where(o => o.Total > min).ToList();
        }
        """);

    [Fact]
    public Task MKP008_AMaterializedEnumerable_TheAggregateSaveShape_DoesNotReport() => Verify("""
        class Save
        {
            // AggregateSave: the change tracker's entries are in memory already.
            int Count(AppDbContext ctx, List<Order> orders) =>
                ctx.ChangeTracker.Entries().ToList().Count(e => e.Entity != null)
                + orders.ToList().Count(o => o.Priority > 1);
        }
        """);

    [Fact]
    public Task MKP008_TheFilterInALaterStatement_DoesNotReport() => Verify("""
        class Reads
        {
            List<Order> Large(AppDbContext ctx, decimal min)
            {
                var all = ctx.Orders.ToList();
                return all.Where(o => o.Total > min).ToList();
            }
        }
        """);

    // ---- Edge cases: a filter that may not follow the materialization into SQL --------------------

    // Each predicate sits directly after ToList(): the analyzer checks the filter the materialization feeds.
    [Theory]
    [InlineData("o => o.IsLate(now)")]                                  // a call on the element
    [InlineData("o => Interesting(o)")]                                 // a local method
    [InlineData("Interesting")]                                         // a method group
    [InlineData("o => o.IsLarge")]                                      // a get-only, computed property
    [InlineData("o => o.Label == \"x\"")]                               // [NotMapped]
    [InlineData("o => o.Id == id")]                                     // a strongly typed id's user-defined ==
    [InlineData("o => o.Id.Value == raw")]                              // a read inside a value-converted id
    [InlineData("o => o.Customer.Name == \"a\"")]                       // two hops
    [InlineData("o => o.Customer != null")]                             // a navigation is not a scalar
    [InlineData("o => o.Status is OrderStatus.Open or OrderStatus.Paid")] // a pattern
    [InlineData("o => o.Number?.Length > 1")]                           // ?.
    [InlineData("o => o.Priority > (boxed is int n ? n : 0)")]          // a pattern in the captured part
    [InlineData("o => o.Priority > level")]                             // a user-defined operator
    [InlineData("o => o.Number.ToCharArray().Contains('x')")]           // Contains over the element's own data
    [InlineData("o => o.Rank > 1")]                                     // a non-public property is not mapped by convention
    [InlineData("o => (long)(Level)o.Priority > 1")]                    // user-defined conversions
    public Task MKP008_APredicateThatMayNotTranslate_DoesNotReport(string predicate) => Verify($$"""
        class Reads
        {
            static bool Interesting(Order order) => order.Priority > 2;

            List<Order> Filter(AppDbContext ctx, DateTimeOffset now, OrderId id, Guid raw, object boxed, Level level) =>
                ctx.Orders.ToList().Where({{predicate}}).ToList();
        }

        struct Level
        {
            public static bool operator >(int priority, Level level) => true;
            public static bool operator <(int priority, Level level) => false;
            public static explicit operator Level(int priority) => default;
            public static explicit operator long(Level level) => 0;
        }
        """);

    [Fact]
    public Task MKP008_AQueryAFilterDoesNotCommuteWith_DoesNotReport() => Verify("""
        class Reads
        {
            IEnumerable<Order> Taken(AppDbContext ctx) => ctx.Orders.OrderBy(o => o.Number).Take(10).ToList().Where(o => o.Priority > 1);

            IEnumerable<int> Projected(AppDbContext ctx) => ctx.Orders.Select(o => o.Priority).ToList().Where(p => p > 1);

            IEnumerable<Order> Distinct(AppDbContext ctx) => ctx.Orders.Distinct().ToList().Where(o => o.Priority > 1);

            IEnumerable<Order> FromAParameter(IQueryable<Order> orders) => orders.ToList().Where(o => o.Priority > 1);
        }
        """);

    [Fact]
    public Task MKP008_LastWithoutAnOrdering_DoesNotReport() => Verify("""
        class Reads
        {
            Order Latest(AppDbContext ctx) => ctx.Orders.ToList().Last(o => o.Priority > 0);
        }
        """);

    [Fact]
    public Task MKP008_ABlockBodiedPredicate_OrTheIndexOverload_DoesNotReport() => Verify("""
        class Reads
        {
            IEnumerable<Order> Block(AppDbContext ctx) => ctx.Orders.ToList().Where(o => { return o.Priority > 1; });

            IEnumerable<Order> Indexed(AppDbContext ctx) => ctx.Orders.ToList().Where((o, i) => o.Priority > i);
        }
        """);

    [Fact]
    public Task MKP008_WithoutEntityFrameworkCore_DoesNotReportOrFail() => QueryTest<MaterializeBeforeFilterAnalyzer>.RunAsync("""
        using System.Collections.Generic;
        using System.Linq;

        class Reads
        {
            List<int> Large(IQueryable<int> numbers) => numbers.ToList().Where(n => n > 1).ToList();
        }
        """, StubSet.None, usings: false);
}
