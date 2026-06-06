using MicroKit.Multitenancy;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace MicroKit.Multitenancy.UnitTests.Stores;

public sealed class ConfigurationTenantStoreTests
{
    private static ConfigurationTenantStore BuildStore(params TenantRecord[] tenants)
    {
        var options = Options.Create(new MultitenancyOptions { Tenants = [.. tenants] });
        return new ConfigurationTenantStore(options);
    }

    private static TenantRecord MakeTenant(string name = "Test") => new()
    {
        Id = TenantId.NewId(),
        Name = name,
        IsActive = true,
    };

    [Fact]
    public async Task FindAsync_TenantInOptions_ReturnsSuccess()
    {
        var tenant = MakeTenant();
        var store = BuildStore(tenant);

        var result = await store.FindAsync(tenant.Id);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(tenant.Id);
        result.Value.Name.ShouldBe(tenant.Name);
    }

    [Fact]
    public async Task FindAsync_TenantNotInOptions_ReturnsTenantNotFound()
    {
        var store = BuildStore(MakeTenant());

        var result = await store.FindAsync(TenantId.NewId());

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.TenantNotFound);
    }

    [Fact]
    public async Task ListAllAsync_ReturnsAllConfiguredTenants()
    {
        var t1 = MakeTenant("Alpha");
        var t2 = MakeTenant("Beta");
        var store = BuildStore(t1, t2);

        var list = await store.ListAllAsync();

        list.Count.ShouldBe(2);
        list.ShouldContain(t => t.Name == "Alpha");
        list.ShouldContain(t => t.Name == "Beta");
    }

    [Fact]
    public async Task ListAllAsync_EmptyOptions_ReturnsEmptyList()
    {
        var store = BuildStore();

        var list = await store.ListAllAsync();

        list.ShouldBeEmpty();
    }
}
