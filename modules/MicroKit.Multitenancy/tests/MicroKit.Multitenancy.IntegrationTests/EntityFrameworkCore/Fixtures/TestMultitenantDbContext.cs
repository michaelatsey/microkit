using Microsoft.EntityFrameworkCore;
using MicroKit.Multitenancy;
using MicroKit.Multitenancy.EntityFrameworkCore;

namespace MicroKit.Multitenancy.IntegrationTests.EntityFrameworkCore.Fixtures;

/// <summary>Concrete MultitenantDbContext for integration tests using SQLite in-memory.</summary>
public sealed class TestMultitenantDbContext(
    DbContextOptions<TestMultitenantDbContext> options,
    ITenantContextAccessor accessor)
    : MultitenantDbContext(options, accessor)
{
    public DbSet<TenantTestEntity> Entities { get; set; } = default!;
}
