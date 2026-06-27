using MicroKit.Tenancy;

namespace MicroKit.Tenancy.IntegrationTests.EntityFrameworkCore.Fixtures;

/// <summary>Test entity implementing ITenantEntity for EF Core integration tests.</summary>
public sealed class TenantTestEntity : ITenantEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public TenantId TenantId { get; set; } = default!;
}
