using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Identifiers;
using MicroKit.Persistence.EntityFrameworkCore;
using MicroKit.Persistence.IntegrationTests.PostgreSql;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// #196 D10: <see cref="EfRepository{TAggregate,TId,TContext}.FindAsync"/> loads a whole aggregate
/// by its typed id, tracked — and nothing beyond its boundary. Run on SQLite and PostgreSQL.
/// </summary>
internal static class EfRepositoryAggregateLoadScenarios
{
    public static async Task FindAsync_LoadsEveryEntityOfTheAggregate_AndNothingBeyondIt(Func<Task<OrderStore>> createStoreAsync)
    {
        await using var store = await createStoreAsync();
        var (orderId, customerId) = await store.SeedAsync();

        await using var context = store.CreateContext();
        var sut = new EfRepository<Order, OrderId, OrderDbContext>(context);

        var order = await sut.FindAsync(orderId);

        order.ShouldNotBeNull();
        order.Lines.Count.ShouldBe(2, "a collection of the root");
        order.Lines.Single(l => l.Sku == "BOOK").Notes.Count.ShouldBe(2, "a collection of a child");
        order.Lines.OfType<GiftLine>().ShouldHaveSingleItem().Wrapping.ShouldNotBeNull(
            "a navigation declared on a derived child type");
        order.PrimaryContact.ShouldNotBeNull("two navigations to the same child type: the first");
        order.BillingContact.ShouldNotBeNull("two navigations to the same child type: the second");
        order.Tags.ShouldBeEmpty("a many-to-many navigation reaches reference data: marked outside the aggregate");
        order.Shipping.ShouldNotBeNull().City.ShouldBe("Lyon", "an owned entity");
        order.Lines.ShouldAllBe(l => l.Notes.All(n => n.Line == l), "the back-reference cycle was resolved, not followed");

        order.CustomerId.ShouldBe(customerId);
        order.Customer.ShouldBeNull("another aggregate root is outside the boundary and is never loaded");
        context.ChangeTracker.Entries<Customer>().ShouldBeEmpty();
    }

    public static async Task FindAsync_ReturnsTheAggregateTracked_SoAMutationIsWrittenByTheCommit(Func<Task<OrderStore>> createStoreAsync)
    {
        await using var store = await createStoreAsync();
        var (orderId, _) = await store.SeedAsync();

        await using (var context = store.CreateContext())
        {
            var sut = new EfRepository<Order, OrderId, OrderDbContext>(context);
            var order = (await sut.FindAsync(orderId)).ShouldNotBeNull();

            order.Lines.Single(l => l.Sku == "BOOK").Notes.First().Text = "edited";
            await new EfUnitOfWork<OrderDbContext>(context).CommitAsync();
        }

        await using var verify = store.CreateContext();
        (await verify.Set<LineNote>().CountAsync(n => n.Text == "edited")).ShouldBe(1);
    }

    public static async Task FindAsync_WhenTheAggregateIsAlreadyTracked_ReturnsItWithoutAQuery(Func<Task<OrderStore>> createStoreAsync)
    {
        await using var store = await createStoreAsync();
        var (orderId, _) = await store.SeedAsync();
        var commands = new List<string>();
        await using var context = store.CreateContext(commands);
        var sut = new EfRepository<Order, OrderId, OrderDbContext>(context);
        var first = await sut.FindAsync(orderId);
        commands.Clear();

        var second = await sut.FindAsync(orderId);

        second.ShouldBeSameAs(first);
        commands.ShouldBeEmpty();
    }

    public static async Task FindAsync_OnAContextThatDoesNotTrackByDefault_StillReturnsATrackedAggregate(Func<Task<OrderStore>> createStoreAsync)
    {
        await using var store = await createStoreAsync();
        var (orderId, _) = await store.SeedAsync();

        await using (var context = store.CreateContext(noTrackingByDefault: true))
        {
            var order = (await new EfRepository<Order, OrderId, OrderDbContext>(context).FindAsync(orderId)).ShouldNotBeNull();
            context.Entry(order).State.ShouldBe(EntityState.Unchanged);
            order.Lines.Single(l => l.Sku == "BOOK").Sku = "BOOK-2";
            await new EfUnitOfWork<OrderDbContext>(context).CommitAsync();
        }

        await using var verify = store.CreateContext();
        (await verify.Set<OrderLine>().CountAsync(l => l.Sku == "BOOK-2")).ShouldBe(1);
    }

    public static async Task FindAsync_WithAnOwnedCollectionAndAnotherCollection_SplitsTheLoad(Func<Task<OrderStore>> createStoreAsync)
    {
        await using var store = await createStoreAsync();
        var invoiceId = await store.SeedInvoiceAsync();
        var commands = new List<string>();
        await using var context = store.CreateContext(commands);

        var invoice = await new EfRepository<Invoice, InvoiceId, OrderDbContext>(context).FindAsync(invoiceId);

        invoice.ShouldNotBeNull();
        invoice.Payments.Count.ShouldBe(2);
        invoice.Items.Count.ShouldBe(2);
        commands.Count.ShouldBeGreaterThan(1,
            "an owned collection mapped to a table is a join: with a second collection the load must split");
    }

    public static async Task FindAsync_WhenMissing_ReturnsNull(Func<Task<OrderStore>> createStoreAsync)
    {
        await using var store = await createStoreAsync();
        await store.SeedAsync();
        await using var context = store.CreateContext();

        (await new EfRepository<Order, OrderId, OrderDbContext>(context).FindAsync(new OrderId(Guid.NewGuid())))
            .ShouldBeNull();
    }

    public static async Task FindAsync_WhenTheKeyIsNotASinglePropertyOfTheIdType_ThrowsAnExplicitError(Func<Task<OrderStore>> createStoreAsync)
    {
        await using var store = await createStoreAsync();
        await using var context = store.CreateContext();
        var sut = new EfRepository<Customer, OrderId, OrderDbContext>(context);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await sut.FindAsync(new OrderId(Guid.NewGuid())));

        thrown.Message.ShouldContain("'Customer' cannot be loaded by 'OrderId'");
    }

    public static async Task FindAsync_WithTwoOrMoreCollections_SplitsTheLoad_OtherwiseRunsOneQuery(Func<Task<OrderStore>> createStoreAsync)
    {
        await using var store = await createStoreAsync();
        var (orderId, customerId) = await store.SeedAsync();

        var orderCommands = new List<string>();
        await using (var context = store.CreateContext(orderCommands))
            await new EfRepository<Order, OrderId, OrderDbContext>(context).FindAsync(orderId);

        var customerCommands = new List<string>();
        await using (var context = store.CreateContext(customerCommands))
            await new EfRepository<Customer, CustomerId, OrderDbContext>(context).FindAsync(customerId);

        orderCommands.Count.ShouldBeGreaterThan(1, "Lines and Notes are collections: the load is split");
        customerCommands.Count.ShouldBe(1, "a single collection: one query");
    }
}

public sealed class EfRepositoryAggregateLoadSqliteTests
{
    [Fact]
    public Task FindAsync_LoadsEveryEntityOfTheAggregate_AndNothingBeyondIt() => EfRepositoryAggregateLoadScenarios.FindAsync_LoadsEveryEntityOfTheAggregate_AndNothingBeyondIt(CreateStoreAsync);

    [Fact]
    public Task FindAsync_ReturnsTheAggregateTracked_SoAMutationIsWrittenByTheCommit() => EfRepositoryAggregateLoadScenarios.FindAsync_ReturnsTheAggregateTracked_SoAMutationIsWrittenByTheCommit(CreateStoreAsync);

    [Fact]
    public Task FindAsync_WhenTheAggregateIsAlreadyTracked_ReturnsItWithoutAQuery() => EfRepositoryAggregateLoadScenarios.FindAsync_WhenTheAggregateIsAlreadyTracked_ReturnsItWithoutAQuery(CreateStoreAsync);

    [Fact]
    public Task FindAsync_OnAContextThatDoesNotTrackByDefault_StillReturnsATrackedAggregate() => EfRepositoryAggregateLoadScenarios.FindAsync_OnAContextThatDoesNotTrackByDefault_StillReturnsATrackedAggregate(CreateStoreAsync);

    [Fact]
    public Task FindAsync_WithAnOwnedCollectionAndAnotherCollection_SplitsTheLoad() => EfRepositoryAggregateLoadScenarios.FindAsync_WithAnOwnedCollectionAndAnotherCollection_SplitsTheLoad(CreateStoreAsync);

    [Fact]
    public Task FindAsync_WhenMissing_ReturnsNull() => EfRepositoryAggregateLoadScenarios.FindAsync_WhenMissing_ReturnsNull(CreateStoreAsync);

    [Fact]
    public Task FindAsync_WhenTheKeyIsNotASinglePropertyOfTheIdType_ThrowsAnExplicitError() => EfRepositoryAggregateLoadScenarios.FindAsync_WhenTheKeyIsNotASinglePropertyOfTheIdType_ThrowsAnExplicitError(CreateStoreAsync);

    [Fact]
    public Task FindAsync_WithTwoOrMoreCollections_SplitsTheLoad_OtherwiseRunsOneQuery() => EfRepositoryAggregateLoadScenarios.FindAsync_WithTwoOrMoreCollections_SplitsTheLoad_OtherwiseRunsOneQuery(CreateStoreAsync);

    private static async Task<OrderStore> CreateStoreAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        return await OrderStore.CreateAsync(o => o.UseSqlite(connection), connection.DisposeAsync);
    }
}

[Collection(PostgreSqlSuite.Name)]
public sealed class EfRepositoryAggregateLoadPostgreSqlTests(PostgreSqlFixture fixture)
{
    [DockerRequiredFact]
    public Task FindAsync_LoadsEveryEntityOfTheAggregate_AndNothingBeyondIt() => EfRepositoryAggregateLoadScenarios.FindAsync_LoadsEveryEntityOfTheAggregate_AndNothingBeyondIt(CreateStoreAsync);

    [DockerRequiredFact]
    public Task FindAsync_ReturnsTheAggregateTracked_SoAMutationIsWrittenByTheCommit() => EfRepositoryAggregateLoadScenarios.FindAsync_ReturnsTheAggregateTracked_SoAMutationIsWrittenByTheCommit(CreateStoreAsync);

    [DockerRequiredFact]
    public Task FindAsync_WhenTheAggregateIsAlreadyTracked_ReturnsItWithoutAQuery() => EfRepositoryAggregateLoadScenarios.FindAsync_WhenTheAggregateIsAlreadyTracked_ReturnsItWithoutAQuery(CreateStoreAsync);

    [DockerRequiredFact]
    public Task FindAsync_OnAContextThatDoesNotTrackByDefault_StillReturnsATrackedAggregate() => EfRepositoryAggregateLoadScenarios.FindAsync_OnAContextThatDoesNotTrackByDefault_StillReturnsATrackedAggregate(CreateStoreAsync);

    [DockerRequiredFact]
    public Task FindAsync_WithAnOwnedCollectionAndAnotherCollection_SplitsTheLoad() => EfRepositoryAggregateLoadScenarios.FindAsync_WithAnOwnedCollectionAndAnotherCollection_SplitsTheLoad(CreateStoreAsync);

    [DockerRequiredFact]
    public Task FindAsync_WhenMissing_ReturnsNull() => EfRepositoryAggregateLoadScenarios.FindAsync_WhenMissing_ReturnsNull(CreateStoreAsync);

    [DockerRequiredFact]
    public Task FindAsync_WhenTheKeyIsNotASinglePropertyOfTheIdType_ThrowsAnExplicitError() => EfRepositoryAggregateLoadScenarios.FindAsync_WhenTheKeyIsNotASinglePropertyOfTheIdType_ThrowsAnExplicitError(CreateStoreAsync);

    [DockerRequiredFact]
    public Task FindAsync_WithTwoOrMoreCollections_SplitsTheLoad_OtherwiseRunsOneQuery() => EfRepositoryAggregateLoadScenarios.FindAsync_WithTwoOrMoreCollections_SplitsTheLoad_OtherwiseRunsOneQuery(CreateStoreAsync);

    private async Task<OrderStore> CreateStoreAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = "orders_" + Guid.NewGuid().ToString("N"),
        }.ConnectionString;

        OrderStore? store = null;
        store = await OrderStore.CreateAsync(o => o.UseNpgsql(connectionString), async () =>
        {
            await using var context = store!.CreateContext();
            await context.Database.EnsureDeletedAsync();
        });
        return store;
    }
}

/// <summary>Creates contexts on one database, seeded with an order aggregate and a customer aggregate.</summary>
public sealed class OrderStore : IAsyncDisposable
{
    private readonly Action<DbContextOptionsBuilder> _configure;
    private readonly Func<ValueTask> _cleanup;

    private OrderStore(Action<DbContextOptionsBuilder> configure, Func<ValueTask> cleanup)
    {
        _configure = configure;
        _cleanup = cleanup;
    }

    public static async Task<OrderStore> CreateAsync(Action<DbContextOptionsBuilder> configure, Func<ValueTask> cleanup)
    {
        var store = new OrderStore(configure, cleanup);
        await using var context = store.CreateContext();
        await context.Database.EnsureCreatedAsync();
        return store;
    }

    public OrderDbContext CreateContext(List<string>? commands = null, bool noTrackingByDefault = false)
    {
        var builder = new DbContextOptionsBuilder<OrderDbContext>();
        _configure(builder);
        builder.UseMicroKitPersistence();
        // The loads must never trip EF Core's row-limit or cartesian-explosion warnings.
        builder.ConfigureWarnings(w => w.Throw(
            CoreEventId.RowLimitingOperationWithoutOrderByWarning,
            RelationalEventId.MultipleCollectionIncludeWarning));
        if (noTrackingByDefault)
            builder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        if (commands is not null)
            builder.LogTo(commands.Add, [RelationalEventId.CommandExecuting]);
        return new OrderDbContext(builder.Options);
    }

    public async Task<(OrderId Order, CustomerId Customer)> SeedAsync()
    {
        var customer = new Customer { Id = new CustomerId(Guid.NewGuid()), Name = "ACME" };
        customer.Addresses.Add(new CustomerAddress { Street = "1 rue de la Paix" });

        var gift = new Tag { Label = "gift" };
        var priority = new Tag { Label = "priority" };
        var book = new OrderLine { Sku = "BOOK" };
        book.Notes.Add(new LineNote { Text = "first edition" });
        book.Notes.Add(new LineNote { Text = "signed" });

        var order = new Order
        {
            Id = new OrderId(Guid.NewGuid()),
            CustomerId = customer.Id,
            PrimaryContact = new Contact { Name = "Ada" },
            BillingContact = new Contact { Name = "Grace" },
            Shipping = new ShippingAddress { City = "Lyon" },
        };
        order.Lines.Add(book);
        order.Lines.Add(new GiftLine { Sku = "PEN", Wrapping = new GiftWrapping { Paper = "gold" } });
        order.Tags.Add(gift);
        order.Tags.Add(priority);

        await using var context = CreateContext();
        context.AddRange(customer, order);
        await context.SaveChangesAsync();
        return (order.Id, customer.Id);
    }

    public async Task<InvoiceId> SeedInvoiceAsync()
    {
        var invoice = new Invoice { Id = new InvoiceId(Guid.NewGuid()) };
        invoice.Payments.Add(new Payment { Amount = 10 });
        invoice.Payments.Add(new Payment { Amount = 20 });
        invoice.Items.Add(new InvoiceItem { Label = "a" });
        invoice.Items.Add(new InvoiceItem { Label = "b" });

        await using var context = CreateContext();
        context.Add(invoice);
        await context.SaveChangesAsync();
        return invoice.Id;
    }

    public ValueTask DisposeAsync() => _cleanup();
}

public readonly record struct InvoiceId(Guid Value) : IEntityId;

/// <summary>An aggregate with an owned collection (a table join) and a regular collection.</summary>
public sealed class Invoice : IAggregateRoot
{
    public InvoiceId Id { get; init; }

    public List<Payment> Payments { get; } = [];

    public List<InvoiceItem> Items { get; } = [];
}

public sealed class Payment
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public decimal Amount { get; set; }
}

public sealed class InvoiceItem
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Label { get; set; } = string.Empty;
}

public readonly record struct OrderId(Guid Value) : IEntityId;

public readonly record struct CustomerId(Guid Value) : IEntityId;

/// <summary>An aggregate root. <see cref="Customer"/> is another aggregate, referenced by id and navigation.</summary>
public sealed class Order : IAggregateRoot
{
    public OrderId Id { get; init; }

    public CustomerId CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public List<OrderLine> Lines { get; } = [];

    public Contact? PrimaryContact { get; set; }

    public Contact? BillingContact { get; set; }

    public List<Tag> Tags { get; } = [];

    public ShippingAddress? Shipping { get; set; }
}

public class OrderLine
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Sku { get; set; } = string.Empty;

    public Order? Order { get; set; }

    public List<LineNote> Notes { get; } = [];
}

/// <summary>A derived child type whose own navigation must be loaded too.</summary>
public sealed class GiftLine : OrderLine
{
    public GiftWrapping? Wrapping { get; set; }
}

public sealed class GiftWrapping
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Paper { get; set; } = string.Empty;
}

/// <summary>A grand-child, with a back-reference to its parent that closes a cycle.</summary>
public sealed class LineNote
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Text { get; set; } = string.Empty;

    public OrderLine? Line { get; set; }
}

public sealed class Contact
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
}

/// <summary>Reached through a many-to-many skip navigation.</summary>
public sealed class Tag
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Label { get; set; } = string.Empty;
}

public sealed class ShippingAddress
{
    public string City { get; set; } = string.Empty;
}

public sealed class Customer : IAggregateRoot
{
    public CustomerId Id { get; init; }

    public string Name { get; set; } = string.Empty;

    public List<CustomerAddress> Addresses { get; } = [];
}

public sealed class CustomerAddress
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Street { get; set; } = string.Empty;
}

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).HasConversion(id => id.Value, v => new OrderId(v)).ValueGeneratedNever();
            b.Property(o => o.CustomerId).HasConversion(id => id.Value, v => new CustomerId(v));
            b.HasOne(o => o.Customer).WithMany().HasForeignKey(o => o.CustomerId);
            b.HasMany(o => o.Lines).WithOne(l => l.Order).HasForeignKey("OrderId");
            // One-to-one (a unique foreign key on the order): each contact row has one order.
            b.HasOne(o => o.PrimaryContact).WithOne().HasForeignKey<Order>("PrimaryContactId");
            b.HasOne(o => o.BillingContact).WithOne().HasForeignKey<Order>("BillingContactId");
            b.HasMany(o => o.Tags).WithMany();
            b.Navigation(o => o.Tags).IsOutsideAggregate(); // many-to-many: tags are shared reference data
            b.OwnsOne(o => o.Shipping);
        });

        modelBuilder.Entity<OrderLine>(b =>
        {
            b.Property(l => l.Id).ValueGeneratedNever();
            b.HasMany(l => l.Notes).WithOne(n => n.Line).HasForeignKey("LineId");
        });

        modelBuilder.Entity<GiftLine>().HasOne(g => g.Wrapping).WithOne().HasForeignKey<GiftLine>("WrappingId");
        modelBuilder.Entity<GiftWrapping>().Property(w => w.Id).ValueGeneratedNever();
        modelBuilder.Entity<LineNote>().Property(n => n.Id).ValueGeneratedNever();
        modelBuilder.Entity<Contact>().Property(c => c.Id).ValueGeneratedNever();
        modelBuilder.Entity<Tag>().Property(t => t.Id).ValueGeneratedNever();

        modelBuilder.Entity<Customer>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasConversion(id => id.Value, v => new CustomerId(v)).ValueGeneratedNever();
            b.HasMany(c => c.Addresses).WithOne().HasForeignKey("CustomerId");
        });
        modelBuilder.Entity<CustomerAddress>().Property(a => a.Id).ValueGeneratedNever();

        modelBuilder.Entity<Invoice>(b =>
        {
            b.HasKey(i => i.Id);
            b.Property(i => i.Id).HasConversion(id => id.Value, v => new InvoiceId(v)).ValueGeneratedNever();
            b.OwnsMany(i => i.Payments, p =>
            {
                p.HasKey(x => x.Id);
                p.Property(x => x.Id).ValueGeneratedNever();
            });
            b.HasMany(i => i.Items).WithOne().HasForeignKey("InvoiceId");
        });
        modelBuilder.Entity<InvoiceItem>().Property(i => i.Id).ValueGeneratedNever();
    }
}
