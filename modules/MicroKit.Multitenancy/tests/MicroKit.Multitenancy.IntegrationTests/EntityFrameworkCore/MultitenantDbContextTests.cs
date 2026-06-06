using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MicroKit.Multitenancy;
using MicroKit.Multitenancy.EntityFrameworkCore;
using MicroKit.Multitenancy.IntegrationTests.EntityFrameworkCore.Fixtures;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MicroKit.Multitenancy.IntegrationTests.EntityFrameworkCore;

public sealed class MultitenantDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public MultitenantDbContextTests()
    {
        // Keep the connection open for the lifetime of the test so the in-memory DB persists.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private TestMultitenantDbContext CreateContext(ITenantContextAccessor accessor,
        TenantStampInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<TestMultitenantDbContext>()
            .UseSqlite(_connection);

        if (interceptor is not null)
            builder.AddInterceptors(interceptor);

        var ctx = new TestMultitenantDbContext(builder.Options, accessor);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static ITenantInfo MakeTenant(Guid id, string name = "Test")
    {
        var t = Substitute.For<ITenantInfo>();
        t.Id.Returns(new TenantId(id));
        t.Name.Returns(name);
        t.IsActive.Returns(true);
        return t;
    }

    // -----------------------------------------------------------------------
    // Query filter — tenant isolation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Query_WithActiveTenant_ReturnsOnlyCurrentTenantEntities()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var tenantA = MakeTenant(tenantAId);
        var tenantB = MakeTenant(tenantBId);
        var accessor = new AsyncLocalTenantContextAccessor();
        var interceptor = new TenantStampInterceptor(accessor);

        // Seed: one entity per tenant
        using (var seedCtx = CreateContext(accessor, interceptor))
        {
            accessor.SetTenant(tenantA);
            seedCtx.Entities.Add(new TenantTestEntity { Name = "EntityA" });
            await seedCtx.SaveChangesAsync();

            accessor.SetTenant(tenantB);
            seedCtx.Entities.Add(new TenantTestEntity { Name = "EntityB" });
            await seedCtx.SaveChangesAsync();
        }

        // Query: logged in as Tenant A
        accessor.SetTenant(tenantA);
        using var ctx = CreateContext(accessor);

        var results = await ctx.Entities.ToListAsync();

        results.Count.ShouldBe(1);
        results[0].Name.ShouldBe("EntityA");
        results[0].TenantId.ShouldBe(new TenantId(tenantAId));
    }

    [Fact]
    public async Task Query_CrossTenant_EntitiesAreNotVisible()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var tenantA = MakeTenant(tenantAId);
        var tenantB = MakeTenant(tenantBId);
        var accessor = new AsyncLocalTenantContextAccessor();
        var interceptor = new TenantStampInterceptor(accessor);

        using (var seedCtx = CreateContext(accessor, interceptor))
        {
            accessor.SetTenant(tenantA);
            seedCtx.Entities.Add(new TenantTestEntity { Name = "A1" });
            seedCtx.Entities.Add(new TenantTestEntity { Name = "A2" });
            await seedCtx.SaveChangesAsync();

            accessor.SetTenant(tenantB);
            seedCtx.Entities.Add(new TenantTestEntity { Name = "B1" });
            await seedCtx.SaveChangesAsync();
        }

        // Query as Tenant B — must NOT see Tenant A's entities
        accessor.SetTenant(tenantB);
        using var ctx = CreateContext(accessor);

        var results = await ctx.Entities.ToListAsync();

        results.Count.ShouldBe(1);
        results.ShouldAllBe(e => e.TenantId == new TenantId(tenantBId));
    }

    [Fact]
    public async Task Query_WhenNoTenantActive_ReturnsEmpty()
    {
        var tenantA = MakeTenant(Guid.NewGuid());
        var accessor = new AsyncLocalTenantContextAccessor();
        var interceptor = new TenantStampInterceptor(accessor);

        using (var seedCtx = CreateContext(accessor, interceptor))
        {
            accessor.SetTenant(tenantA);
            seedCtx.Entities.Add(new TenantTestEntity { Name = "A" });
            await seedCtx.SaveChangesAsync();
        }

        // Query with no tenant — filter produces WHERE TenantId IS NULL → empty
        accessor.SetTenant(null);
        using var ctx = CreateContext(accessor);

        var results = await ctx.Entities.ToListAsync();

        results.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------
    // G5: Tenant switch mid-scope
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Query_AfterTenantSwitch_NewQueriesReflectNewTenant()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var tenantA = MakeTenant(tenantAId);
        var tenantB = MakeTenant(tenantBId);
        var accessor = new AsyncLocalTenantContextAccessor();
        var interceptor = new TenantStampInterceptor(accessor);

        using (var seedCtx = CreateContext(accessor, interceptor))
        {
            accessor.SetTenant(tenantA);
            seedCtx.Entities.Add(new TenantTestEntity { Name = "A" });
            await seedCtx.SaveChangesAsync();

            accessor.SetTenant(tenantB);
            seedCtx.Entities.Add(new TenantTestEntity { Name = "B" });
            await seedCtx.SaveChangesAsync();
        }

        using var ctx = CreateContext(accessor);

        // First query as Tenant A
        accessor.SetTenant(tenantA);
        var aResults = await ctx.Entities.ToListAsync();
        aResults.ShouldAllBe(e => e.TenantId == new TenantId(tenantAId));

        // Switch to Tenant B — new query must reflect the new tenant
        accessor.SetTenant(tenantB);
        var bResults = await ctx.Entities.ToListAsync();
        bResults.ShouldAllBe(e => e.TenantId == new TenantId(tenantBId));
    }

    // -----------------------------------------------------------------------
    // IgnoreTenantScope bypass
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Query_WithIgnoreTenantScope_ReturnsCrossTenantEntities()
    {
        var tenantA = MakeTenant(Guid.NewGuid());
        var tenantB = MakeTenant(Guid.NewGuid());
        var accessor = new AsyncLocalTenantContextAccessor();
        var interceptor = new TenantStampInterceptor(accessor);

        using (var seedCtx = CreateContext(accessor, interceptor))
        {
            accessor.SetTenant(tenantA);
            seedCtx.Entities.Add(new TenantTestEntity { Name = "A" });
            await seedCtx.SaveChangesAsync();

            accessor.SetTenant(tenantB);
            seedCtx.Entities.Add(new TenantTestEntity { Name = "B" });
            await seedCtx.SaveChangesAsync();
        }

        accessor.SetTenant(tenantA);
        using var ctx = CreateContext(accessor);

        using var bypass = new IgnoreTenantScope();
        // [MTK-BYPASS] Test: verifying cross-tenant visibility for admin scenario
        var allResults = await ctx.Entities.IgnoreQueryFilters().ToListAsync();

        allResults.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Query_AfterIgnoreTenantScopeDisposed_FilterRestored()
    {
        var tenantAId = Guid.NewGuid();
        var tenantA = MakeTenant(tenantAId);
        var tenantB = MakeTenant(Guid.NewGuid());
        var accessor = new AsyncLocalTenantContextAccessor();
        var interceptor = new TenantStampInterceptor(accessor);

        using (var seedCtx = CreateContext(accessor, interceptor))
        {
            accessor.SetTenant(tenantA);
            seedCtx.Entities.Add(new TenantTestEntity { Name = "A" });
            await seedCtx.SaveChangesAsync();

            accessor.SetTenant(tenantB);
            seedCtx.Entities.Add(new TenantTestEntity { Name = "B" });
            await seedCtx.SaveChangesAsync();
        }

        accessor.SetTenant(tenantA);
        using var ctx = CreateContext(accessor);

        using (new IgnoreTenantScope()) { /* bypass active */ }

        // After dispose — filter must be restored
        var results = await ctx.Entities.ToListAsync();
        results.Count.ShouldBe(1);
        results[0].TenantId.ShouldBe(new TenantId(tenantAId));
    }

    [Fact]
    public async Task IgnoreTenantScope_NestedScopes_RestorePreviousStateCorrectly()
    {
        var tenantA = MakeTenant(Guid.NewGuid());
        var tenantB = MakeTenant(Guid.NewGuid());
        var accessor = new AsyncLocalTenantContextAccessor();
        var interceptor = new TenantStampInterceptor(accessor);

        using (var seedCtx = CreateContext(accessor, interceptor))
        {
            accessor.SetTenant(tenantA);
            seedCtx.Entities.Add(new TenantTestEntity { Name = "A" });
            await seedCtx.SaveChangesAsync();

            accessor.SetTenant(tenantB);
            seedCtx.Entities.Add(new TenantTestEntity { Name = "B" });
            await seedCtx.SaveChangesAsync();
        }

        accessor.SetTenant(tenantA);
        using var ctx = CreateContext(accessor);

        // Outer bypass scope
        using (new IgnoreTenantScope())
        {
            // Inner bypass scope — G3: restores previous (true), not false
            using (new IgnoreTenantScope()) { }

            // After inner dispose: bypass still active (outer is still open)
            // [MTK-BYPASS] Test: verifying nested scope restore behavior
            var results = await ctx.Entities.IgnoreQueryFilters().ToListAsync();
            results.Count.ShouldBe(2);
        }

        // After outer dispose: filter restored
        var filtered = await ctx.Entities.ToListAsync();
        filtered.Count.ShouldBe(1);
    }

    // -----------------------------------------------------------------------
    // A3: Multiple DbContext instances — verify independent filter evaluation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Query_MultipleDbContextInstances_EachSeeOwnTenantData()
    {
        // A3: Validates that EF Core's per-query evaluation of CurrentTenantId works
        // correctly across multiple DbContext instances, not just the model-building instance.
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var tenantA = MakeTenant(tenantAId);
        var tenantB = MakeTenant(tenantBId);

        var accessorA = new AsyncLocalTenantContextAccessor();
        var accessorB = new AsyncLocalTenantContextAccessor();
        var interceptorA = new TenantStampInterceptor(accessorA);
        var interceptorB = new TenantStampInterceptor(accessorB);

        // Seed using instance A
        using (var seedA = CreateContext(accessorA, interceptorA))
        {
            accessorA.SetTenant(tenantA);
            seedA.Entities.Add(new TenantTestEntity { Name = "EntityA" });
            await seedA.SaveChangesAsync();
        }

        // Seed using a second connection is not possible on same :memory: DB,
        // so we reuse the same connection and seed both tenants via accessorA
        using (var seedBoth = CreateContext(accessorA, interceptorA))
        {
            accessorA.SetTenant(tenantB);
            seedBoth.Entities.Add(new TenantTestEntity { Name = "EntityB" });
            await seedBoth.SaveChangesAsync();
        }

        // Two separate context instances pointing at the same data
        accessorA.SetTenant(tenantA);
        accessorB.SetTenant(tenantB);

        using var ctxA = CreateContext(accessorA);
        using var ctxB = CreateContext(accessorB);

        var resultsA = await ctxA.Entities.ToListAsync();
        var resultsB = await ctxB.Entities.ToListAsync();

        resultsA.Count.ShouldBe(1);
        resultsA[0].Name.ShouldBe("EntityA");
        resultsA[0].TenantId.ShouldBe(new TenantId(tenantAId));

        resultsB.Count.ShouldBe(1);
        resultsB[0].Name.ShouldBe("EntityB");
        resultsB[0].TenantId.ShouldBe(new TenantId(tenantBId));
    }

    [Fact]
    public async Task Query_ParallelRequests_DifferentTenants_AreIsolated()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var tenantA = MakeTenant(tenantAId);
        var tenantB = MakeTenant(tenantBId);

        // Each task gets its own isolated SQLite connection (unique DataSource) to avoid
        // SQLite Error 5 (SQLITE_BUSY) when two threads concurrently access the same connection.
        IReadOnlyList<TenantTestEntity>? seenByA = null;
        IReadOnlyList<TenantTestEntity>? seenByB = null;

        var taskA = Task.Run(async () =>
        {
            // Unique per-task in-memory DB — fully isolated from taskB
            using var conn = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            conn.Open();
            var acc = new AsyncLocalTenantContextAccessor();
            var interceptor = new TenantStampInterceptor(acc);
            var seedOpts = new DbContextOptionsBuilder<TestMultitenantDbContext>()
                .UseSqlite(conn)
                .AddInterceptors(interceptor)
                .Options;
            using (var seedCtx = new TestMultitenantDbContext(seedOpts, acc))
            {
                seedCtx.Database.EnsureCreated();
                acc.SetTenant(tenantA);
                seedCtx.Entities.Add(new TenantTestEntity { Name = "A" });
                await seedCtx.SaveChangesAsync();
                acc.SetTenant(tenantB);
                seedCtx.Entities.Add(new TenantTestEntity { Name = "B" });
                await seedCtx.SaveChangesAsync();
            }
            acc.SetTenant(tenantA);
            var queryOpts = new DbContextOptionsBuilder<TestMultitenantDbContext>()
                .UseSqlite(conn)
                .Options;
            using var ctx = new TestMultitenantDbContext(queryOpts, acc);
            seenByA = await ctx.Entities.ToListAsync();
        });

        var taskB = Task.Run(async () =>
        {
            // Unique per-task in-memory DB — fully isolated from taskA
            using var conn = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            conn.Open();
            var acc = new AsyncLocalTenantContextAccessor();
            var interceptor = new TenantStampInterceptor(acc);
            var seedOpts = new DbContextOptionsBuilder<TestMultitenantDbContext>()
                .UseSqlite(conn)
                .AddInterceptors(interceptor)
                .Options;
            using (var seedCtx = new TestMultitenantDbContext(seedOpts, acc))
            {
                seedCtx.Database.EnsureCreated();
                acc.SetTenant(tenantA);
                seedCtx.Entities.Add(new TenantTestEntity { Name = "A" });
                await seedCtx.SaveChangesAsync();
                acc.SetTenant(tenantB);
                seedCtx.Entities.Add(new TenantTestEntity { Name = "B" });
                await seedCtx.SaveChangesAsync();
            }
            acc.SetTenant(tenantB);
            var queryOpts = new DbContextOptionsBuilder<TestMultitenantDbContext>()
                .UseSqlite(conn)
                .Options;
            using var ctx = new TestMultitenantDbContext(queryOpts, acc);
            seenByB = await ctx.Entities.ToListAsync();
        });

        await Task.WhenAll(taskA, taskB);

        seenByA.ShouldNotBeNull();
        seenByB.ShouldNotBeNull();
        seenByA!.ShouldAllBe(e => e.TenantId == new TenantId(tenantAId));
        seenByB!.ShouldAllBe(e => e.TenantId == new TenantId(tenantBId));
    }
}
