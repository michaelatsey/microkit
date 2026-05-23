using BenchmarkDotNet.Attributes;
using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Events;
using MicroKit.Domain.ValueObjects.Common;
using MicroKit.Domain.Identifiers;

namespace MicroKit.Domain.Benchmarks;

/// <summary>
/// Benchmarks for aggregate operations including event collection and business rule validation.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class AggregateBenchmarks
{
    private readonly BenchmarkOrderId _orderId;
    private readonly BenchmarkCustomerId _customerId;
    private readonly List<BenchmarkOrderItemRequest> _orderItems;

    public AggregateBenchmarks()
    {
        _orderId = BenchmarkOrderId.New();
        _customerId = BenchmarkCustomerId.New();
        _orderItems = new List<BenchmarkOrderItemRequest>
        {
            new(BenchmarkProductId.New(), 2, new Money(29.99m, "USD")),
            new(BenchmarkProductId.New(), 1, new Money(45.00m, "USD")),
            new(BenchmarkProductId.New(), 3, new Money(12.50m, "USD"))
        };
    }

    [Benchmark(Description = "Create Order with Items")]
    public TestOrder CreateOrderWithItems()
    {
        return TestOrder.Place(_customerId, _orderItems);
    }

    [Benchmark(Description = "Add Multiple Items to Order")]
    public TestOrder AddItemsToOrder()
    {
        var order = TestOrder.Create(_orderId, _customerId);

        foreach (var item in _orderItems)
        {
            order.AddItem(item.ProductId, item.Quantity, item.UnitPrice);
        }

        return order;
    }

    [Benchmark(Description = "Collect Domain Events")]
    public IReadOnlyCollection<IDomainEvent> CollectDomainEvents()
    {
        var order = TestOrder.Place(_customerId, _orderItems);
        return order.DomainEvents;
    }

    [Benchmark(Description = "Drain Domain Events")]
    public IReadOnlyCollection<IDomainEvent> DrainDomainEvents()
    {
        var order = TestOrder.Place(_customerId, _orderItems);
        return order.DrainDomainEvents();
    }

    [Benchmark(Description = "Multiple Event Operations")]
    public int MultipleEventOperations()
    {
        var order = TestOrder.Place(_customerId, _orderItems);

        // Add more items (generates events)
        for (int i = 0; i < 5; i++)
        {
            order.AddItem(BenchmarkProductId.New(), 1, new Money(10.00m, "USD"));
        }

        // Ship order (generates event)
        order.Ship(TrackingNumber.Create($"TRK{DateTime.Now.Ticks}"));

        // Drain and count
        var events = order.DrainDomainEvents();
        return events.Count;
    }
}


public sealed class TestOrder : AggregateRoot<BenchmarkOrderId>
{
    private readonly List<OrderItem> _items = [];

    private TestOrder(BenchmarkOrderId id, BenchmarkCustomerId customerId) : base(id)
    {
        CustomerId = customerId;
        Status = OrderStatus.Draft;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public BenchmarkCustomerId CustomerId { get; private init; }
    public OrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset? ShippedAt { get; private set; }
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    public Money TotalAmount => _items.Count == 0
        ? Money.Zero("USD")
        : _items.Select(i => i.TotalPrice).Aggregate((a, b) => a.Add(b));

    public static TestOrder Create(BenchmarkOrderId id, BenchmarkCustomerId customerId)
    {
        return new TestOrder(id, customerId);
    }

    public static TestOrder Place(BenchmarkCustomerId customerId, IReadOnlyList<BenchmarkOrderItemRequest> itemRequests)
    {
        var order = new TestOrder(BenchmarkOrderId.New(), customerId);

        foreach (var request in itemRequests)
        {
            order.AddItem(request.ProductId, request.Quantity, request.UnitPrice);
        }

        order.Status = OrderStatus.Placed;
        order.RaiseDomainEvent(new OrderPlacedEvent(order.Id, customerId, order.TotalAmount));

        return order;
    }

    public void AddItem(BenchmarkProductId productId, int quantity, Money unitPrice)
    {
        var item = new OrderItem(productId, quantity, unitPrice);
        _items.Add(item);

        RaiseDomainEvent(new OrderItemAddedEvent(Id, productId, quantity, unitPrice));
    }

    public void Ship(TrackingNumber trackingNumber)
    {
        Status = OrderStatus.Shipped;
        ShippedAt = DateTimeOffset.UtcNow;

        RaiseDomainEvent(new OrderShippedEvent(Id, trackingNumber));
    }
}

public sealed class OrderItem
{
    public OrderItem(BenchmarkProductId productId, int quantity, Money unitPrice)
    {
        ProductId = productId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public BenchmarkProductId ProductId { get; }
    public int Quantity { get; }
    public Money UnitPrice { get; }
    public Money TotalPrice => UnitPrice.Multiply(Quantity);
}

