using Microsoft.EntityFrameworkCore;
using MicroKit.Tenancy;
using MicroKit.Tenancy.EntityFrameworkCore;

namespace MicroKit.Tenancy.IntegrationTests.EntityFrameworkCore.Fixtures;

/// <summary>Concrete MultitenantDbContext for integration tests using SQLite in-memory.</summary>
public sealed class TestMultitenantDbContext(
    DbContextOptions<TestMultitenantDbContext> options,
    ITenantContextAccessor accessor)
    : MultitenantDbContext(options, accessor)
{
    public DbSet<TenantTestEntity> Entities { get; set; } = default!;
}
