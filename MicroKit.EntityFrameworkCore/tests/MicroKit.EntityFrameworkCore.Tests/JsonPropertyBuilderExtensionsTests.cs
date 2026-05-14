using Microsoft.EntityFrameworkCore;

namespace MicroKit.EntityFrameworkCore.Tests;

public sealed class JsonPropertyBuilderExtensionsTests
{
    private sealed record Address(string Street, string City);

    private sealed class Order
    {
        public int Id { get; set; }
        public Address? ShippingAddress { get; set; }
        public List<string> Tags { get; set; } = [];
    }

    private sealed class OrderContext : DbContext
    {
        private readonly Action<ModelBuilder>? _onModelCreating;

        public DbSet<Order> Orders => Set<Order>();

        public OrderContext(DbContextOptions<OrderContext> options, Action<ModelBuilder>? onModelCreating = null)
            : base(options)
        {
            _onModelCreating = onModelCreating;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>(b =>
            {
                b.HasKey(o => o.Id);
                b.Property(o => o.ShippingAddress).HasJsonConversion();
                b.Property(o => o.Tags).HasJsonConversion();
            });

            _onModelCreating?.Invoke(modelBuilder);
        }
    }

    private static DbContextOptions<OrderContext> BuildOptions(string dbName) =>
        new DbContextOptionsBuilder<OrderContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

    // ── HasJsonConversion — persists and restores value ───────────────────────

    [Fact]
    public async Task HasJsonConversion_PersistsComplexType_AndRestores()
    {
        var options = BuildOptions(nameof(HasJsonConversion_PersistsComplexType_AndRestores));
        var address = new Address("10 Downing St", "London");

        await using (var ctx = new OrderContext(options))
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Orders.Add(new Order { Id = 1, ShippingAddress = address });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = new OrderContext(options))
        {
            var order = await ctx.Orders.FindAsync(1);
            Assert.NotNull(order);
            Assert.Equal(address, order.ShippingAddress);
        }
    }

    [Fact]
    public async Task HasJsonConversion_PersistsCollection_AndRestores()
    {
        var options = BuildOptions(nameof(HasJsonConversion_PersistsCollection_AndRestores));
        var tags = new List<string> { "urgent", "vip" };

        await using (var ctx = new OrderContext(options))
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Orders.Add(new Order { Id = 2, Tags = tags });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = new OrderContext(options))
        {
            var order = await ctx.Orders.FindAsync(2);
            Assert.NotNull(order);
            Assert.Equal(tags, order.Tags);
        }
    }

    // ── HasJsonConversion — change tracking detects modifications ─────────────

    [Fact]
    public async Task HasJsonConversion_ChangeTracking_DetectsModification()
    {
        var options = BuildOptions(nameof(HasJsonConversion_ChangeTracking_DetectsModification));

        await using (var ctx = new OrderContext(options))
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Orders.Add(new Order { Id = 3, ShippingAddress = new Address("1 First St", "Alpha") });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = new OrderContext(options))
        {
            var order = await ctx.Orders.FindAsync(3);
            Assert.NotNull(order);
            order.ShippingAddress = new Address("2 Second Ave", "Beta");
            var changes = ctx.ChangeTracker.Entries<Order>()
                .Count(e => e.State == EntityState.Modified);
            Assert.Equal(1, changes);
        }
    }

    // ── HasJsonConversion — with custom options ────────────────────────────────

    [Fact]
    public async Task HasJsonConversion_WithCustomOptions_RoundTrips()
    {
        const string dbName = nameof(HasJsonConversion_WithCustomOptions_RoundTrips);
        var customOptions = new JsonSerializerOptions { WriteIndented = true };

        var dbOptions = new DbContextOptionsBuilder<OrderContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        using var ctx = new OrderContext(dbOptions, mb =>
        {
            mb.Entity<Order>(b =>
            {
                b.HasKey(o => o.Id);
                b.Property(o => o.ShippingAddress).HasJsonConversion(customOptions);
                b.Property(o => o.Tags).HasJsonConversion(customOptions);
            });
        });

        await ctx.Database.EnsureCreatedAsync();
        var order = new Order { Id = 10, ShippingAddress = new Address("99 Oak", "Riverside") };
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var loaded = await ctx.Orders.FindAsync(10);
        Assert.Equal(order.ShippingAddress, loaded!.ShippingAddress);
    }
}
