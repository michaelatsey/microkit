using MicroKit.Persistence.Analyzers.Tests.Stubs;

namespace MicroKit.Persistence.Analyzers.Tests;

/// <summary>MKP007 — a query run on each iteration of a <c>foreach</c> or <c>for</c> loop (#197 phase 6, ADR-014).</summary>
public sealed class QueryInLoopAnalyzerTests
{
    private static Task Verify(string code, StubSet stubs = StubSet.All) =>
        QueryTest<QueryInLoopAnalyzer>.RunAsync(code, stubs);

    // ---- Violations ----------------------------------------------------------------------------

    [Fact]
    public Task MKP007_AnEfExecutorInAForeach_Reports() => Verify("""
        class Handler
        {
            async Task Run(AppDbContext ctx, List<OrderId> ids)
            {
                foreach (var id in ids)
                {
                    var order = await ctx.Orders.{|MKP007:FirstOrDefaultAsync(o => o.Id == id)|};
                    _ = order;
                }
            }
        }
        """);

    [Fact]
    public Task MKP007_RepositoryReadsInAForeach_Report() => Verify("""
        class Handler
        {
            async Task Run(IOrderRepository orders, IOrderReadRepository reads, List<OrderId> ids, List<string> numbers)
            {
                foreach (var id in ids)
                {
                    _ = await orders.{|MKP007:FindAsync(id)|};
                    _ = await reads.{|MKP007:CountAsync(new QueryOptions<Order>())|};
                }

                foreach (var number in numbers)
                    _ = await orders.{|MKP007:FindByNumberAsync(number)|};
            }
        }
        """);

    [Fact]
    public Task MKP007_ExplicitLoadingInAnAwaitForeach_Reports() => Verify("""
        class Handler
        {
            async Task Run(AppDbContext ctx)
            {
                await foreach (var order in ctx.Orders.AsAsyncEnumerable())
                    await ctx.Entry(order).Collection(o => o.Lines).{|MKP007:LoadAsync()|};
            }
        }
        """);

    [Fact]
    public Task MKP007_FindInAForLoop_AndANestedForeachOverASet_Report() => Verify("""
        class Handler
        {
            void Run(AppDbContext ctx, object[] keys)
            {
                for (var i = 0; i < keys.Length; i++)
                {
                    _ = ctx.Orders.{|MKP007:Find(keys[i])|};
                    _ = ctx.{|MKP007:Find<Order>(keys[i])|};
                    foreach (var order in {|MKP007:ctx.Orders|})
                        _ = order;
                }
            }
        }
        """);

    [Fact]
    public Task MKP007_ALinqExecutorOverAQuery_Reports() => Verify("""
        class Handler
        {
            void Run(AppDbContext ctx, string[] numbers)
            {
                foreach (var number in numbers)
                {
                    _ = ctx.Orders.Where(o => o.Number == number).{|MKP007:ToList()|};
                    _ = ctx.Orders.{|MKP007:Count(o => o.Number == number)|};
                    _ = ctx.Orders.AsEnumerable().{|MKP007:First()|};
                }
            }
        }
        """);

    // ---- Valid code ----------------------------------------------------------------------------

    [Fact]
    public Task MKP007_TheQueryBeforeTheLoop_OrTheLoopOverTheQuery_DoesNotReport() => Verify("""
        class Handler
        {
            async Task Run(AppDbContext ctx, List<OrderId> ids, CancellationToken ct)
            {
                var orders = await ctx.Orders.Where(o => ids.Contains(o.Id)).ToListAsync(ct);
                foreach (var order in orders)
                    _ = order;

                foreach (var order in await ctx.Orders.ToListAsync(ct))
                    _ = order;

                // EfRepository.LoadAsync: the loop enumerates the query once.
                var loaded = new List<Order>();
                await foreach (var order in ctx.Orders.AsAsyncEnumerable().WithCancellation(ct).ConfigureAwait(false))
                    loaded.Add(order);
            }
        }
        """);

    [Fact]
    public Task MKP007_ALoopThatOnlyBuildsTheQuery_DoesNotReport() => Verify("""
        class Repository
        {
            // EfRepository.IncludeAggregate: the loop composes; the query runs once, after it.
            Task<List<Order>> Load(AppDbContext ctx, string[] paths, List<Expression<Func<Order, bool>>> filters)
            {
                IQueryable<Order> query = ctx.Orders;
                foreach (var path in paths)
                    query = query.Include(path);
                foreach (var filter in filters)
                    query = query.Where(filter).OrderBy(o => o.Number);
                return query.ToListAsync();
            }
        }
        """);

    [Fact]
    public Task MKP007_StagingInALoop_DoesNotReport() => Verify("""
        class Handler
        {
            async Task Run(IOrderRepository orders, List<Order> batch)
            {
                foreach (var order in batch)
                {
                    await orders.AddAsync(order);
                    await orders.DeleteAsync(order);
                    _ = orders.GetVersion(order);
                }

                for (var i = 0; i < 3; i++)
                {
                    await orders.AddRangeAsync(batch);
                    await orders.RemoveExpiredAsync();
                }
            }
        }
        """);

    [Fact]
    public Task MKP007_ABulkWriteInALoop_IsNotARead_DoesNotReport() => Verify("""
        class Handler
        {
            async Task Run(AppDbContext ctx, string[] numbers)
            {
                foreach (var number in numbers)
                    await ctx.Orders.Where(o => o.Number == number).ExecuteDeleteAsync();
            }
        }
        """);

    [Fact]
    public Task MKP007_SyncExplicitLoading_AndANestedAwaitForeachOverAQuery_Report() => Verify("""
        class Handler
        {
            async Task Run(AppDbContext ctx, List<Order> orders, CancellationToken ct)
            {
                foreach (var order in orders)
                {
                    ctx.Entry(order).Reference(o => o.Customer).{|MKP007:Load()|};
                    await foreach (var other in {|MKP007:ctx.Orders.AsAsyncEnumerable().WithCancellation(ct)|})
                        _ = other;
                }
            }
        }
        """);

    [Fact]
    public Task MKP007_AForLoopWithoutACondition_IsTheRetryIdiom_DoesNotReport() => Verify("""
        class Handler
        {
            async Task<Order?> Find(IOrderRepository orders, OrderId id)
            {
                for (var attempt = 1; ; attempt++)
                {
                    try { return await orders.FindAsync(id); }
                    catch (TimeoutException) when (attempt < 3) { }
                }
            }
        }
        """);

    [Fact]
    public Task MKP007_AWhileBatchingLoop_DoesNotReport() => Verify("""
        class Worker
        {
            async Task Drain(AppDbContext ctx, CancellationToken ct)
            {
                while (!ct.IsCancellationRequested)
                {
                    var batch = await ctx.Orders.Where(o => o.Status == OrderStatus.Open).ToListAsync(ct);
                    if (batch.Count == 0)
                        break;
                }

                do
                {
                    _ = await ctx.Orders.AnyAsync(ct);
                }
                while (false);
            }
        }
        """);

    [Fact]
    public Task MKP007_LinqToObjectsInALoop_DoesNotReport() => Verify("""
        class Handler
        {
            void Run(List<Order> orders, string[] numbers)
            {
                foreach (var number in numbers)
                {
                    _ = orders.First(o => o.Number == number);
                    _ = orders.Where(o => o.Number == number).ToList();
                    _ = orders.AsQueryable().Count(o => o.Number == number);
                    _ = orders.AsQueryable().Where(o => o.Number == number).ToList();
                }
            }
        }
        """);

    // ---- Edge cases ----------------------------------------------------------------------------

    [Fact]
    public Task MKP007_AQueryInAForCondition_RunsOnEachIteration_Reports() => Verify("""
        class Handler
        {
            void Run(AppDbContext ctx)
            {
                for (var i = 0; i < ctx.Orders.{|MKP007:Count()|}; i++)
                    _ = i;

                for (var page = ctx.Orders.Count(); page > 0; page--)
                    _ = page;
            }
        }
        """);

    [Fact]
    public Task MKP007_AnInnerForeachOverAQuery_InsideAnOuterLoop_Reports() => Verify("""
        class Handler
        {
            async Task Run(AppDbContext ctx, string[] numbers)
            {
                foreach (var number in numbers)
                {
                    foreach (var order in await ctx.Orders.Where(o => o.Number == number).{|MKP007:ToListAsync()|})
                        _ = order;
                }
            }
        }
        """);

    [Fact]
    public Task MKP007_AKeysetPageWalk_IsOneQueryPerPage_DoesNotReport() => Verify("""
        class Export
        {
            async Task Run(IOrderReadRepository reads)
            {
                for (var page = 0; page < 10; page++)
                {
                    _ = await reads.ListKeysetAsync(new QueryOptions<Order>());
                    _ = await reads.ListPagedAsync(new QueryOptions<Order>());
                }
            }
        }
        """);

    [Fact]
    public Task MKP007_InsideALambdaDeclaredInTheLoop_DoesNotReport() => Verify("""
        class Handler
        {
            void Run(AppDbContext ctx, string[] numbers, List<Func<int>> later)
            {
                foreach (var number in numbers)
                    later.Add(() => ctx.Orders.Count(o => o.Number == number));
            }
        }
        """);

    [Fact]
    public Task MKP007_InsideALocalFunctionDeclaredInTheLoop_DoesNotReport() => Verify("""
        class Handler
        {
            void Run(AppDbContext ctx, string[] numbers)
            {
                foreach (var number in numbers)
                {
                    int Count() => ctx.Orders.Count(o => o.Number == number);
                    _ = number;
                }
            }
        }
        """);

    [Fact]
    public Task MKP007_ALoopInsideALambda_Reports_AndNestedLoopsReportOnce() => Verify("""
        class Handler
        {
            Action Run(AppDbContext ctx, string[] numbers) => () =>
            {
                foreach (var number in numbers)
                    foreach (var other in numbers)
                        _ = ctx.Orders.{|MKP007:Any(o => o.Number == number + other)|};
            };
        }
        """);

    [Fact]
    public Task MKP007_WithoutEntityFrameworkCore_RepositoryReadsStillReport() => QueryTest<QueryInLoopAnalyzer>.RunAsync(
        QueryStubs.Usings.Replace("using Microsoft.EntityFrameworkCore;", "") + """
        class Handler
        {
            async Task Run(IOrderRepository orders, List<OrderId> ids)
            {
                foreach (var id in ids)
                    _ = await orders.{|MKP007:FindAsync(id, 3)|};
            }
        }
        """, StubSet.MicroKit | StubSet.Shop, usings: false);
}
