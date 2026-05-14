using Microsoft.EntityFrameworkCore;
using MicroKit.Messaging.Persistence.EFCore.Extensions;
using MicroKit.Idempotency.EFCore.Extensions;
using OrderApi.Domain;
using OrderApi.Domain.Orders;
using OrderApi.Infrastructure.Persistence.Configurations;

namespace OrderApi.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyMessagingConfigurations(this);
        modelBuilder.ApplyMicroKitIdempotencyConfigurations(this);
    }

    public new Task<int> SaveChangesAsync(CancellationToken ct = default)
        => base.SaveChangesAsync(ct);
}
