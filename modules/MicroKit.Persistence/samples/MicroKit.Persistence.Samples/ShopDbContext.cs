namespace MicroKit.Persistence.Samples;

// README: "The DbContext"
public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.AddDomainConventions(typeof(OrderId).Assembly);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            order.Ignore(o => o.DomainEvents);
            order.HasMany(o => o.Lines).WithOne().HasForeignKey("OrderId");
        });

        // The domain assigns the line's Guid key: without this, EF Core treats a Guid key as
        // generated and tracks a new line with a key already set as Modified, not Added.
        modelBuilder.Entity<OrderLine>().Property(l => l.Id).ValueGeneratedNever();
    }
}
