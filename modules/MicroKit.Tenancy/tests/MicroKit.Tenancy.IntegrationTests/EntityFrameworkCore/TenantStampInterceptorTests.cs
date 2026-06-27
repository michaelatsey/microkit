using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MicroKit.Tenancy;
using MicroKit.Tenancy.EntityFrameworkCore;
using MicroKit.Tenancy.IntegrationTests.EntityFrameworkCore.Fixtures;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MicroKit.Tenancy.IntegrationTests.EntityFrameworkCore;

public sealed class TenantStampInterceptorTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public TenantStampInterceptorTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (TestMultitenantDbContext ctx, AsyncLocalTenantContextAccessor accessor) CreateContext()
    {
        var accessor = new AsyncLocalTenantContextAccessor();
        var interceptor = new TenantStampInterceptor(accessor);
        var options = new DbContextOptionsBuilder<TestMultitenantDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptor)
            .Options;
        var ctx = new TestMultitenantDbContext(options, accessor);
        ctx.Database.EnsureCreated();
        return (ctx, accessor);
    }

    private static ITenantInfo MakeTenant(Guid id)
    {
        var t = Substitute.For<ITenantInfo>();
        t.Id.Returns(new TenantId(id));
        return t;
    }

    // -----------------------------------------------------------------------
    // G4: Always stamps current tenant
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SavingChangesAsync_StampsTenantId_OnAddedEntity()
    {
        var tenantId = Guid.NewGuid();
        var (ctx, accessor) = CreateContext();
        using var _ = ctx;

        accessor.SetTenant(MakeTenant(tenantId));
        ctx.Entities.Add(new TenantTestEntity { Name = "New" });
        await ctx.SaveChangesAsync();

        var saved = await ctx.Entities.IgnoreQueryFilters().SingleAsync();
        saved.TenantId.ShouldBe(new TenantId(tenantId));
    }

    [Fact]
    public async Task SavingChangesAsync_AlwaysStamps_CurrentTenantId_OverwritingExistingValue()
    {
        // G4: Even if the caller manually set a TenantId before Add(), the interceptor
        // overwrites it with the current accessor value. The current tenant context wins.
        var realTenantId = Guid.NewGuid();
        var staleTenantId = Guid.NewGuid();

        var (ctx, accessor) = CreateContext();
        using var _ = ctx;

        accessor.SetTenant(MakeTenant(realTenantId));

        var entity = new TenantTestEntity
        {
            Name = "Stale",
            TenantId = new TenantId(staleTenantId), // manually set — will be overwritten
        };
        ctx.Entities.Add(entity);
        await ctx.SaveChangesAsync();

        var saved = await ctx.Entities.IgnoreQueryFilters().SingleAsync();
        saved.TenantId.ShouldBe(new TenantId(realTenantId), "interceptor must override stale manual TenantId");
        saved.TenantId.ShouldNotBe(new TenantId(staleTenantId));
    }

    [Fact]
    public async Task SavingChangesAsync_Throws_WhenNoActiveTenant()
    {
        var (ctx, accessor) = CreateContext();
        using var _ = ctx;

        // No tenant set — accessor.GetTenant() returns null
        accessor.SetTenant(null);

        ctx.Entities.Add(new TenantTestEntity { Name = "Orphan" });

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => ctx.SaveChangesAsync());
        ex.Message.ShouldContain("Cannot save changes without an active tenant context");
    }

    [Fact]
    public async Task SavingChanges_Sync_StampsTenantId()
    {
        // Verifies the synchronous SavingChanges override is also covered.
        var tenantId = Guid.NewGuid();
        var (ctx, accessor) = CreateContext();
        using var _ = ctx;

        accessor.SetTenant(MakeTenant(tenantId));
        ctx.Entities.Add(new TenantTestEntity { Name = "Sync" });
        ctx.SaveChanges(); // synchronous path

        var saved = await ctx.Entities.IgnoreQueryFilters().SingleAsync();
        saved.TenantId.ShouldBe(new TenantId(tenantId));
    }

    [Fact]
    public async Task SavingChangesAsync_DoesNotInterfere_WithNonTenantEntities()
    {
        // If a DbContext has non-ITenantEntity types, the interceptor should leave them alone.
        // Since TestMultitenantDbContext only has TenantTestEntity, we verify zero side-effects
        // on unrelated updates (Modified state — interceptor only processes Added).
        var tenantId = Guid.NewGuid();
        var (ctx, accessor) = CreateContext();
        using var _ = ctx;

        accessor.SetTenant(MakeTenant(tenantId));
        ctx.Entities.Add(new TenantTestEntity { Name = "Original" });
        await ctx.SaveChangesAsync();

        // [MTK-BYPASS] Test: fetching all to update name
        var entity = await ctx.Entities.IgnoreQueryFilters().SingleAsync();
        entity.Name = "Updated";
        await ctx.SaveChangesAsync(); // Modified state — interceptor must not re-stamp

        var updated = await ctx.Entities.IgnoreQueryFilters().SingleAsync();
        updated.Name.ShouldBe("Updated");
        updated.TenantId.ShouldBe(new TenantId(tenantId));
    }
}
