using MicroKit.Sample.OrderApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using MicroKit.Messaging.Persistence.EFCore.Extensions;
using MicroKit.Idempotency.EFCore;

namespace MicroKit.Sample.OrderApi.Infrastructure;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        //builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        modelBuilder.ApplyMessagingConfigurations(this);
    }



}
