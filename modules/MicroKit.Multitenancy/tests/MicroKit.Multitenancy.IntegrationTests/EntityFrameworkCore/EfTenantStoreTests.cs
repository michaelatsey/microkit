using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MicroKit.Multitenancy;
using MicroKit.Multitenancy.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace MicroKit.Multitenancy.IntegrationTests.EntityFrameworkCore;

public sealed class EfTenantStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantStoreDbContext _context;

    public EfTenantStoreTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TenantStoreDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new TenantStoreDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private EfTenantStore CreateStore() => new(_context);

    private async Task SeedTenant(Guid id, string name, bool isActive = true)
    {
        _context.Tenants.Add(new EfTenantRecord
        {
            Id = id,
            Name = name,
            IsActive = isActive,
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear(); // ensure next reads are fresh
    }

    // -----------------------------------------------------------------------
    // FindAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FindAsync_WhenTenantExists_ReturnsSuccess()
    {
        var id = Guid.NewGuid();
        await SeedTenant(id, "Acme");
        var store = CreateStore();

        var result = await store.FindAsync(new TenantId(id));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(new TenantId(id));
        result.Value.Name.ShouldBe("Acme");
        result.Value.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task FindAsync_WhenTenantNotFound_ReturnsFailure()
    {
        var store = CreateStore();

        var result = await store.FindAsync(TenantId.NewId());

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.TenantNotFound);
    }

    [Fact]
    public async Task FindAsync_WhenTenantInactive_ReturnsSuccessWithInactiveTenant()
    {
        // The store returns data as-is. Callers decide policy on inactive tenants.
        var id = Guid.NewGuid();
        await SeedTenant(id, "Inactive Corp", isActive: false);
        var store = CreateStore();

        var result = await store.FindAsync(new TenantId(id));

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task FindAsync_WithCancellation_ThrowsOperationCancelled()
    {
        var store = CreateStore();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => store.FindAsync(TenantId.NewId(), cts.Token).AsTask());
    }

    // -----------------------------------------------------------------------
    // ListAllAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListAllAsync_ReturnsAllRegisteredTenants()
    {
        await SeedTenant(Guid.NewGuid(), "Alpha");
        await SeedTenant(Guid.NewGuid(), "Beta");
        await SeedTenant(Guid.NewGuid(), "Gamma");
        var store = CreateStore();

        var tenants = await store.ListAllAsync();

        tenants.Count.ShouldBe(3);
        tenants.Select(t => t.Name).ShouldContain("Alpha");
        tenants.Select(t => t.Name).ShouldContain("Beta");
        tenants.Select(t => t.Name).ShouldContain("Gamma");
    }

    [Fact]
    public async Task ListAllAsync_WhenEmpty_ReturnsEmptyList()
    {
        var store = CreateStore();

        var tenants = await store.ListAllAsync();

        tenants.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListAllAsync_IncludesBothActiveAndInactiveTenants()
    {
        await SeedTenant(Guid.NewGuid(), "Active Tenant", isActive: true);
        await SeedTenant(Guid.NewGuid(), "Inactive Tenant", isActive: false);
        var store = CreateStore();

        var tenants = await store.ListAllAsync();

        tenants.Count.ShouldBe(2);
        tenants.ShouldContain(t => t.IsActive);
        tenants.ShouldContain(t => !t.IsActive);
    }

    // -----------------------------------------------------------------------
    // EfTenantRecord schema (A2: verify IEntityTypeConfiguration applied)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EfTenantRecord_IsNotFilteredByTenant_CrossTenantVisibility()
    {
        // G2 verification: TenantStoreDbContext does NOT inherit MultitenantDbContext,
        // so no tenant query filter is applied to TenantRecord. All tenants are always visible.
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        await SeedTenant(id1, "T1");
        await SeedTenant(id2, "T2");

        var allRecords = await _context.Tenants.ToListAsync();

        allRecords.Count.ShouldBe(2);
    }
}
