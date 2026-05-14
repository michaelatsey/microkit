using MicroKit.Domain.Abstractions;
using OrderApi.Domain.Orders.Events;
using OrderApi.Domain.Orders.ValueObjects;

namespace OrderApi.Domain.Orders;

public sealed class Order : AggregateRootBase<Guid>
{
    private readonly List<OrderItem> _items = [];

    public string TenantId { get; private set; } = string.Empty;
    public string CustomerId { get; private set; } = string.Empty;
    public OrderStatus Status { get; private set; }
    public Money TotalAmount { get; private set; } = Money.Zero;
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    public DateTimeOffset PlacedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    // Parameterless constructor for ORM deserialization
    private Order() { }

    private Order(Guid id, string tenantId, string customerId) : base(id)
    {
        TenantId = tenantId;
        CustomerId = customerId;
    }

    public static Order Place(Guid id, string tenantId, string customerId, IEnumerable<OrderItem> items)
    {
        var order = new Order(id, tenantId, customerId)
        {
            Status = OrderStatus.Placed,
            PlacedAt = DateTimeOffset.UtcNow
        };

        foreach (var item in items)
            order._items.Add(item);

        order.TotalAmount = Money.Sum(order._items.Select(i => i.Subtotal));
        order.AddDomainEvent(new OrderPlacedEvent(id, tenantId, customerId, order.TotalAmount));
        order.IncrementVersion();
        return order;
    }

    public void Confirm()
    {
        if (Status != OrderStatus.Placed)
            throw new InvalidOperationException($"Cannot confirm order in status {Status}.");

        Status = OrderStatus.Confirmed;
        ConfirmedAt = DateTimeOffset.UtcNow;
        AddDomainEvent(new OrderConfirmedEvent(Id, TenantId));
        IncrementVersion();
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("Order is already cancelled.");
        if (Status == OrderStatus.Confirmed)
            throw new InvalidOperationException("Cannot cancel a confirmed order.");

        Status = OrderStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        IncrementVersion();
    }
}
