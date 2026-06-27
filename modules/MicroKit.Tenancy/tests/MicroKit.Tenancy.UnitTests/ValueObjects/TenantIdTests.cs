using MicroKit.Tenancy;
using Shouldly;
using Xunit;

namespace MicroKit.Tenancy.UnitTests.ValueObjects;

public sealed class TenantIdTests
{
    [Fact]
    public void NewId_CalledTwice_ReturnsDifferentIds()
    {
        var first = Tenancy.TenantId.NewId();
        var second = Tenancy.TenantId.NewId();

        first.ShouldNotBe(second);
    }

    [Fact]
    public void RecordEquality_SameGuid_AreEqual()
    {
        var guid = Guid.NewGuid();
        var a = new Tenancy.TenantId(guid);
        var b = new Tenancy.TenantId(guid);

        a.ShouldBe(b);
    }

    [Fact]
    public void RecordEquality_DifferentGuids_AreNotEqual()
    {
        var a = new Tenancy.TenantId(Guid.NewGuid());
        var b = new Tenancy.TenantId(Guid.NewGuid());

        a.ShouldNotBe(b);
    }

    [Fact]
    public void ToString_ReturnsParsableGuidString()
    {
        var guid = Guid.NewGuid();
        var tenantId = new Tenancy.TenantId(guid);

        var str = tenantId.ToString();

        Guid.TryParse(str, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(guid);
    }

    [Fact]
    public void Value_ReturnsConstructedGuid()
    {
        var guid = Guid.NewGuid();
        var tenantId = new Tenancy.TenantId(guid);

        tenantId.Value.ShouldBe(guid);
    }
}
