using MicroKit.Multitenancy;
using Shouldly;
using Xunit;

namespace MicroKit.Multitenancy.UnitTests.TenantId;

public sealed class TenantIdTests
{
    [Fact]
    public void NewId_CalledTwice_ReturnsDifferentIds()
    {
        var first = Multitenancy.TenantId.NewId();
        var second = Multitenancy.TenantId.NewId();

        first.ShouldNotBe(second);
    }

    [Fact]
    public void RecordEquality_SameGuid_AreEqual()
    {
        var guid = Guid.NewGuid();
        var a = new Multitenancy.TenantId(guid);
        var b = new Multitenancy.TenantId(guid);

        a.ShouldBe(b);
    }

    [Fact]
    public void RecordEquality_DifferentGuids_AreNotEqual()
    {
        var a = new Multitenancy.TenantId(Guid.NewGuid());
        var b = new Multitenancy.TenantId(Guid.NewGuid());

        a.ShouldNotBe(b);
    }

    [Fact]
    public void ToString_ReturnsParsableGuidString()
    {
        var guid = Guid.NewGuid();
        var tenantId = new Multitenancy.TenantId(guid);

        var str = tenantId.ToString();

        Guid.TryParse(str, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(guid);
    }

    [Fact]
    public void Value_ReturnsConstructedGuid()
    {
        var guid = Guid.NewGuid();
        var tenantId = new Multitenancy.TenantId(guid);

        tenantId.Value.ShouldBe(guid);
    }
}
