namespace MicroKit.Persistence.Samples;

public sealed record CatalogItemId : EntityId<CatalogItemId>
{
    public CatalogItemId(Guid value) : base(value) { }
}

/// <summary>Reference data shared by many aggregates: not an aggregate root, not part of one.</summary>
public sealed class Country
{
    public string Code { get; private set; } = string.Empty;
}

public sealed class CatalogItem : AggregateRoot<CatalogItemId>
{
    private CatalogItem(CatalogItemId id) : base(id) { }

    public string? OriginCode { get; private set; }

    public Country? Origin { get; private set; }
}

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.AddDomainConventions(typeof(CatalogItemId).Assembly);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Country>().HasKey(c => c.Code);

        // README: "The aggregate boundary"
        modelBuilder.Entity<CatalogItem>(item =>
        {
            item.Ignore(i => i.DomainEvents);
            item.HasOne(i => i.Origin).WithMany().HasForeignKey(i => i.OriginCode);
            item.Navigation(i => i.Origin).IsOutsideAggregate(); // reference data: never loaded with the item
        });
    }
}

public static class ContextOptions
{
    // README: "The aggregate boundary" — a context configured without the EfCoreBuilder
    public static DbContextOptions<ShopDbContext> ForShop(string connectionString) =>
        new DbContextOptionsBuilder<ShopDbContext>()
            .UseNpgsql(connectionString)
            .UseMicroKitPersistence()
            .Options;
}
