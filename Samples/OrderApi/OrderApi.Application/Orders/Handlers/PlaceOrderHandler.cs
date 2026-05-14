using MicroKit.Cqrs.MediatR.Handlers;
using MicroKit.Messaging.Abstractions.Outbox;
using OrderApi.Application.Orders.Commands;
using OrderApi.Application.Orders.Dtos;
using OrderApi.Domain;
using OrderApi.Domain.Orders;
using OrderApi.Domain.Orders.Events;
using OrderApi.Domain.Orders.Repositories;
using OrderApi.Domain.Orders.ValueObjects;

namespace OrderApi.Application.Orders.Handlers;

public sealed class PlaceOrderHandler(
    IOrderRepository orders,
    IOutboxService outbox,
    IUnitOfWork uow)
    : CommandHandler<PlaceOrderCommand, Guid>
{
    public override async Task<Guid> HandleAsync(PlaceOrderCommand cmd, CancellationToken ct = default)
    {
        var items = cmd.Items.Select(i =>
            new OrderItem(i.ProductId, i.ProductName, i.Quantity, Money.Of(i.UnitPrice, i.Currency)));

        var order = Order.Place(cmd.OrderId, cmd.TenantId, cmd.CustomerId, items);

        await orders.AddAsync(order, ct);

        var @event = order.DomainEvents.OfType<OrderPlacedEvent>().Single();
        await outbox.EnqueueAsync(
            cmd.TenantId,
            cmd.OrderId.ToString(),
            @event,
            new OutboxDestination { PublishAsNotification = true },
            cancellationToken: ct);

        order.ClearDomainEvents();
        await uow.SaveChangesAsync(ct);

        return order.Id;
    }
}
