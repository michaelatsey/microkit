// The C# blocks of the module README that need persistence and MicroKit.MediatR, compiled here
// (#162, #187) — this project already references every package they name. Every README block
// marked <!-- snippet: name --> is the text between "// begin-snippet: name" and
// "// end-snippet" in this file or in tests/MicroKit.Messaging.UnitTests/Readme/ReadmeSnippets.cs;
// ReadmeSnippetTests in both projects fails when they differ. Compiled, never run against a
// database. Edit both together.
#pragma warning disable CA1050, CA1062, IDE0060
namespace MicroKit.Messaging.MediatR.IntegrationTests.Readme;

using MicroKit.MediatR.Behaviors.DependencyInjection;
using MicroKit.Messaging.MediatR.DependencyInjection;
using MicroKit.Persistence.EntityFrameworkCore;
using static MicroKit.Result.Result;

// begin-snippet: durable-context
// Your DbContext owns the outbox and inbox tables — that is what makes the write atomic.
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyMessagingConfiguration();
}
// end-snippet

// begin-snippet: publishing-contract
// Business payload only. Everything about delivery — identity, tenant, correlation, timestamps —
// is assigned when the row is staged. The attribute is the wire name and is mandatory: a CLR type
// name cannot serve as one, because a consumer in another service holds a different type.
[MessageContract("shop.orders.order-placed")]   // wire type: shop.orders.order-placed.v1
public sealed record OrderPlaced(Guid OrderId, Guid CustomerId) : IIntegrationEvent;
// end-snippet

// begin-snippet: durable-end-to-end
// The fact.
public sealed record OrderPlacedEvent(Guid OrderId, Guid CustomerId, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);

// The outbox payload. Declaring it is what puts the event on the outbox; an event with no
// notification is dispatched to its handlers and goes no further.
public sealed class OrderPlacedNotification(OrderPlacedEvent domainEvent)
    : DomainEventNotification<OrderPlacedEvent>(domainEvent);

// The command raises the event and stages the aggregate. It does not save, and does not publish.
public sealed class PlaceOrderHandler(IOrderRepository repo)
    : ICommandHandler<PlaceOrderCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(PlaceOrderCommand command, CancellationToken ct = default)
    {
        var order = Order.Place(command.CustomerId);
        order.Raise(new OrderPlacedEvent(order.Id, command.CustomerId, DateTimeOffset.UtcNow));
        await repo.AddAsync(order, ct);
        return Success(order.Id);
    }
}

// Runs after commit, from the outbox processor. Must be idempotent — it may run more than once.
public sealed class ProjectOrderHandler(IReadModel readModel)
    : INotificationHandler<OrderPlacedNotification>
{
    public Task Handle(OrderPlacedNotification n, CancellationToken cancellationToken)
        => readModel.UpsertOrderAsync(n.DomainEvent.OrderId, cancellationToken);
}
// end-snippet

// The application types the README examples name, declared minimally so the examples compile.
public sealed record PlaceOrderCommand(Guid CustomerId) : ICommand<Result<Guid>>;

public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken ct);
}

public interface IReadModel
{
    Task UpsertOrderAsync(Guid orderId, CancellationToken ct);
}

public sealed class Order
{
    private readonly List<IDomainEvent> _events = [];

    public Guid Id { get; } = Guid.NewGuid();

    public static Order Place(Guid customerId) => new();

    public void Raise(IDomainEvent domainEvent) => _events.Add(domainEvent);
}

internal static class ReadmeSnippets
{
    public static void DurableComposition(IServiceCollection services, string connectionString)
    {
        // begin-snippet: durable-composition
        // Persistence: AddUnitOfWork binds IUnitOfWork / ITransactionalContext and registers the
        // IDomainEventsProvider the dispatcher drains — all on the same DbContext.
        services.AddMicroKitPersistence(p => p
            .AddEntityFrameworkCore()
            .AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString))
            .AddUnitOfWork<AppDbContext>());

        // CQRS. TransactionBehavior (order 700) is the dispatch-and-commit owner: it dispatches domain
        // events and only then commits, so outbox rows land in the same SaveChanges as the aggregate.
        services.AddMicroKitMediatR(cfg => cfg
            .FromAssemblyContaining<PlaceOrderCommand>()
            .AddTransactionBehavior());

        // Messaging, durable: domain events go through the outbox. No transport, and none needed.
        services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "shop.orders";
            bus.AddEfCoreOutbox<AppDbContext>();
            bus.AddMediatRDomainEvents();    // only with MicroKit.Messaging.MediatR
        });
        // end-snippet
    }

    public static void PublishingComposition(IServiceCollection services)
    {
        // begin-snippet: publishing-composition
        services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "shop.orders";
            bus.Publishes<OrderPlaced>();
            bus.AddEfCoreOutbox<AppDbContext>();
            bus.AddEfCoreIntegrationEvents<AppDbContext>();   // the outbox writer and IIntegrationEventPublisher
            bus.UseInMemoryTransport();                       // or a broker provider's transport
        });
        // end-snippet
    }
}
