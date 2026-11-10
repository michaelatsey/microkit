using MicroKit.Persistence.Analyzers.Tests.Stubs;

namespace MicroKit.Persistence.Analyzers.Tests;

/// <summary>MKP006 — two or more collection includes in one query without a split decision (#197 phase 6, ADR-014).</summary>
public sealed class CollectionIncludeAnalyzerTests
{
    private static Task Verify(string code, StubSet stubs = StubSet.All) =>
        QueryTest<CollectionIncludeAnalyzer>.RunAsync(code, stubs);

    // ---- Violations ----------------------------------------------------------------------------

    [Fact]
    public Task MKP006_TwoSiblingCollections_OnADbSet_ReportsTheSecondInclude() => Verify("""
        class Reads
        {
            Task<List<Order>> All(AppDbContext ctx) =>
                ctx.Orders.Include(o => o.Lines).{|MKP006:Include(o => o.Payments)|}.ToListAsync();
        }
        """);

    [Fact]
    public Task MKP006_ACollectionAndANestedCollection_Reports() => Verify("""
        class Reads
        {
            async Task<Order?> One(AppDbContext ctx) =>
                await ctx.Set<Order>().AsNoTracking()
                    .Include(o => o.Lines).{|MKP006:ThenInclude(l => l.Taxes)|}
                    .FirstOrDefaultAsync().ConfigureAwait(false);
        }
        """);

    [Fact]
    public Task MKP006_TwoCollectionsInTheIncludesOfQueryOptions_WithoutASplit_Reports() => Verify("""
        class Reads
        {
            QueryOptions<Order> Detailed() => new QueryOptions<Order>()
                .WithIncludes(q => q.Include(o => o.Lines).{|MKP006:Include(o => o.Payments)|});
        }
        """);

    [Fact]
    public Task MKP006_TwoCollectionsAssignedToIncludes_WithoutASplit_Reports() => Verify("""
        class Reads
        {
            QueryOptions<Order> Initializer() => new QueryOptions<Order>
            {
                Includes = q => q.Include(o => o.Lines).{|MKP006:Include(o => o.Payments)|},
            };

            QueryOptions<Order> With(QueryOptions<Order> opts) =>
                opts with { Includes = q => q.Include(o => o.Lines).{|MKP006:Include(o => o.Payments)|} };
        }
        """);

    [Fact]
    public Task MKP006_IsASuggestion_TheSeverityIsInfo()
    {
        var test = new CSharpAnalyzerTest<CollectionIncludeAnalyzer, CompatXUnitVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.TestState.Sources.Add(("Test.cs", QueryStubs.Usings + """
            class Reads
            {
                Task<List<Order>> All(AppDbContext ctx) =>
                    ctx.Orders.Include(o => o.Lines).Include(o => o.Payments).ToListAsync();
            }
            """));
        test.TestState.Sources.Add(("MicroKit.cs", QueryStubs.MicroKit));
        test.TestState.Sources.Add(("EntityFrameworkCore.cs", QueryStubs.EntityFrameworkCore));
        test.TestState.Sources.Add(("Relational.cs", QueryStubs.Relational));
        test.TestState.Sources.Add(("Shop.cs", QueryStubs.Shop));
        test.TestState.Sources.Add(("ShopContext.cs", QueryStubs.ShopContext));
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(CollectionIncludeAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
                .WithSpan("Test.cs", 17, 42, 17, 66)
                .WithArguments("Lines", "Payments"));
        return test.RunAsync();
    }

    // ---- Valid code ----------------------------------------------------------------------------

    [Fact]
    public Task MKP006_AReferenceInclude_AndOneCollection_DoNotReport() => Verify("""
        class Reads
        {
            Task<List<Order>> All(AppDbContext ctx) =>
                ctx.Orders.Include(o => o.Customer).Include(o => o.Lines).ThenInclude(l => l.Product).ToListAsync();
        }
        """);

    [Fact]
    public Task MKP006_WithAsSplitQuery_BeforeOrAfterTheIncludes_DoesNotReport() => Verify("""
        class Reads
        {
            Task<List<Order>> Before(AppDbContext ctx) =>
                ctx.Orders.AsSplitQuery().Include(o => o.Lines).Include(o => o.Payments).ToListAsync();

            Task<List<Order>> After(AppDbContext ctx) =>
                ctx.Orders.Include(o => o.Lines).Include(o => o.Payments).AsSplitQuery().ToListAsync();
        }
        """);

    [Fact]
    public Task MKP006_WithAsSingleQuery_TheDecisionIsMade_DoesNotReport() => Verify("""
        class Reads
        {
            Task<List<Order>> All(AppDbContext ctx) =>
                ctx.Orders.Include(o => o.Lines).Include(o => o.Payments).AsSingleQuery().ToListAsync();
        }
        """);

    [Fact]
    public Task MKP006_QueryOptionsWithAsSplitQuery_BeforeOrAfter_DoesNotReport() => Verify("""
        class Reads
        {
            QueryOptions<Order> After() => new QueryOptions<Order>()
                .WithIncludes(q => q.Include(o => o.Lines).Include(o => o.Payments))
                .AsSplitQuery();

            QueryOptions<Order> Before() => new QueryOptions<Order>()
                .AsSplitQuery()
                .WithIncludes(q => q.Include(o => o.Lines).Include(o => o.Payments));

            QueryOptions<Order> Initializer() => new QueryOptions<Order>
            {
                Includes = q => q.Include(o => o.Lines).Include(o => o.Payments),
                AsSplitQueryEnabled = true,
            };

            QueryOptions<Order> With(QueryOptions<Order> opts) =>
                opts.WithIncludes(q => q.Include(o => o.Lines).Include(o => o.Payments)) with { AsSplitQueryEnabled = true };

            QueryOptions<Order> SplitFirst(QueryOptions<Order> opts) =>
                (opts with { AsSplitQueryEnabled = true }).WithIncludes(q => q.Include(o => o.Lines).Include(o => o.Payments));
        }
        """);

    [Fact]
    public Task MKP006_TheIncludeAggregateShape_StringPathsThenTheSplitDecision_DoesNotReport() => Verify("""
        class Repository
        {
            // EfRepository.IncludeAggregate: string paths read from the model, then the plan's split decision.
            IQueryable<Order> IncludeAggregate(IQueryable<Order> query, string[] paths, bool isSplit)
            {
                foreach (var path in paths)
                    query = query.Include(path);
                return isSplit ? query.AsSplitQuery() : query;
            }

            Task<List<Order>> Load(AppDbContext ctx) =>
                ctx.Orders.Include("Lines").Include("Payments").ToListAsync();
        }
        """);

    // ---- Edge cases ----------------------------------------------------------------------------

    [Fact]
    public Task MKP006_AFilteredInclude_AndAnEnumerableProperty_AreCollections() => Verify("""
        class Reads
        {
            Task<List<Order>> All(AppDbContext ctx) =>
                ctx.Orders.Include(o => o.Lines.Where(l => l.Quantity > 1)).{|MKP006:Include(o => o.Notes)|}.ToListAsync();
        }
        """);

    [Fact]
    public Task MKP006_TheSameCollectionIncludedTwice_CountsOnce() => Verify("""
        class Reads
        {
            Task<List<Order>> All(AppDbContext ctx) =>
                ctx.Orders.Include(o => o.Lines).ThenInclude(l => l.Product)
                    .Include(o => o.Lines).ThenInclude(l => l.Product)
                    .ToListAsync();
        }
        """);

    [Fact]
    public Task MKP006_OptionsPassedToCountAsync_DropTheIncludes_DoesNotReport() => Verify("""
        class Reads
        {
            ValueTask<int> Count(IOrderReadRepository orders) =>
                orders.CountAsync(new QueryOptions<Order>().WithIncludes(q => q.Include(o => o.Lines).Include(o => o.Payments)));

            ValueTask<bool> Any(IOrderReadRepository orders) =>
                orders.AnyAsync(new QueryOptions<Order>().WithIncludes(q => q.Include(o => o.Lines).Include(o => o.Payments)));

            ValueTask<IReadOnlyList<int>> Projected(IOrderReadRepository orders) =>
                orders.ListAsync(new QueryOptions<Order>().WithIncludes(q => q.Include(o => o.Lines).Include(o => o.Payments)), o => o.Priority);
        }
        """);

    [Fact]
    public Task MKP006_AQueryStoredWithoutExecution_OrRootedAtAParameter_DoesNotReport() => Verify("""
        class Reads
        {
            IQueryable<Order> Stored(AppDbContext ctx)
            {
                var query = ctx.Orders.Include(o => o.Lines).Include(o => o.Payments);
                return query;
            }

            Task<List<Order>> FromParameter(IQueryable<Order> orders) =>
                orders.Include(o => o.Lines).Include(o => o.Payments).ToListAsync();
        }
        """);

    [Fact]
    public Task MKP006_AnIncludeEfCoreIgnores_ACountOrAProjection_DoesNotReport() => Verify("""
        class Reads
        {
            Task<int> Count(AppDbContext ctx) => ctx.Orders.Include(o => o.Lines).Include(o => o.Payments).CountAsync();

            bool Any(AppDbContext ctx) => ctx.Orders.Include(o => o.Lines).Include(o => o.Payments).Any();

            Task<List<OrderId>> Ids(AppDbContext ctx) =>
                ctx.Orders.Include(o => o.Lines).Include(o => o.Payments).Select(o => o.Id).ToListAsync();
        }
        """);

    [Fact]
    public Task MKP006_TheSameChildNameUnderTwoParents_CountsTwice() => Verify("""
        class Reads
        {
            Task<List<Order>> All(AppDbContext ctx) =>
                ctx.Orders.Include(o => o.Customer).ThenInclude(c => c.Addresses)
                    .Include(o => o.Shipper).{|MKP006:ThenInclude(c => c.Addresses)|}
                    .ToListAsync();
        }
        """);

    [Fact]
    public Task MKP006_EnumeratedByAnAwaitForeach_ThroughAsAsyncEnumerable_Reports() => Verify("""
        class Reads
        {
            async Task Print(AppDbContext ctx, CancellationToken ct)
            {
                await foreach (var order in ctx.Orders.Include(o => o.Lines).{|MKP006:Include(o => o.Payments)|}
                                   .AsAsyncEnumerable().WithCancellation(ct).ConfigureAwait(false))
                    _ = order;
            }
        }
        """);

    [Fact]
    public Task MKP006_EnumeratedByAForeach_Reports() => Verify("""
        class Reads
        {
            void Print(AppDbContext ctx)
            {
                foreach (var order in ctx.Orders.Include(o => o.Lines).{|MKP006:Include(o => o.Payments)|})
                    _ = order;
            }
        }
        """);

    [Fact]
    public Task MKP006_WithoutTheRelationalHints_DoesNotReport() => Verify("""
        class Reads
        {
            Task<List<Order>> All(AppDbContext ctx) =>
                ctx.Orders.Include(o => o.Lines).Include(o => o.Payments).ToListAsync();
        }
        """, StubSet.MicroKit | StubSet.EntityFrameworkCore | StubSet.Shop);

    [Fact]
    public Task MKP006_WithoutEntityFrameworkCore_DoesNotReportOrFail() => QueryTest<CollectionIncludeAnalyzer>.RunAsync("""
        using System;
        using System.Collections.Generic;
        using System.Linq;

        static class Look
        {
            public static IQueryable<T> Include<T, TP>(this IQueryable<T> source, Func<T, TP> path) => source;
        }

        class Reads
        {
            List<int[]> All(IQueryable<int[]> rows) => rows.Include(r => r).Include(r => r.ToList()).ToList();
        }
        """, StubSet.None, usings: false);
}
