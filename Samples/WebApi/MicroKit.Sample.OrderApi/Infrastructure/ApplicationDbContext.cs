using MicroKit.Sample.OrderApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using MicroKit.Messaging.Persistence.EFCore.Extensions;
using MicroKit.Idempotency.EFCore;

namespace MicroKit.Sample.OrderApi.Infrastructure;

/// <summary>Application EF Core database context for the sample Order API.</summary>
public class ApplicationDbContext : DbContext
{
    /// <summary>Initializes a new instance.</summary>
    /// <param name="options">The database context options.</param>
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }
    /// <summary>Gets the orders dataset.</summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        //builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        modelBuilder.ApplyMessagingConfigurations(this);
    }



}
