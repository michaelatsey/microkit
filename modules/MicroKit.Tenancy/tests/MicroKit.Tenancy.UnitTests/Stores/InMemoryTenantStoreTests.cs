using MicroKit.Tenancy;
using MicroKit.Result;
using Shouldly;
using Xunit;

namespace MicroKit.Tenancy.UnitTests.Stores;

public sealed class InMemoryTenantStoreTests
{
    private static TenantRecord MakeTenant(string name = "Test") => new()
    {
        Id = TenantId.NewId(),
        Name = name,
        IsActive = true,
    };

    [Fact]
    public async Task FindAsync_ExistingTenant_ReturnsSuccess()
    {
        var tenant = MakeTenant();
        var store = new InMemoryTenantStore([tenant]);

        var result = await store.FindAsync(tenant.Id);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(tenant);
    }

    [Fact]
    public async Task FindAsync_UnknownTenantId_ReturnsTenantNotFound()
    {
        var store = new InMemoryTenantStore();

        var result = await store.FindAsync(TenantId.NewId());

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.TenantNotFound);
    }

    [Fact]
    public async Task ListAllAsync_ReturnsAllRegisteredTenants()
    {
        var t1 = MakeTenant("Alpha");
        var t2 = MakeTenant("Beta");
        var store = new InMemoryTenantStore([t1, t2]);

        var list = await store.ListAllAsync();

        list.Count.ShouldBe(2);
        list.ShouldContain(t1);
        list.ShouldContain(t2);
    }

    [Fact]
    public async Task ListAllAsync_EmptyStore_ReturnsEmptyList()
    {
        var store = new InMemoryTenantStore();

        var list = await store.ListAllAsync();

        list.ShouldBeEmpty();
    }

    [Fact]
    public async Task Constructor_WithInitialTenants_PrePopulates()
    {
        var tenants = new[] { MakeTenant("A"), MakeTenant("B"), MakeTenant("C") };
        var store = new InMemoryTenantStore(tenants);

        var list = await store.ListAllAsync();

        list.Count.ShouldBe(3);
    }

    [Fact]
    public async Task AddTenant_ExistingId_Overwrites()
    {
        var id = TenantId.NewId();
        var original = new TenantRecord { Id = id, Name = "Original" };
        var updated = new TenantRecord { Id = id, Name = "Updated" };
        var store = new InMemoryTenantStore([original]);

        store.AddTenant(updated);
        var result = await store.FindAsync(id);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Updated");
    }
}
