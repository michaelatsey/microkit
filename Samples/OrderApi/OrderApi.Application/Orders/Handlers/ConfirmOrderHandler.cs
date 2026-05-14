using MicroKit.Cqrs.MediatR.Handlers;
using MicroKit.Messaging.Abstractions.Outbox;
using OrderApi.Application.Orders.Commands;
using OrderApi.Domain;
using OrderApi.Domain.Orders.Events;
using OrderApi.Domain.Orders.Repositories;

namespace OrderApi.Application.Orders.Handlers;

public sealed class ConfirmOrderHandler(
    IOrderRepository orders,
    IOutboxService outbox,
    IUnitOfWork uow)
    : CommandHandler<ConfirmOrderCommand, bool>
{
    public override async Task<bool> HandleAsync(ConfirmOrderCommand cmd, CancellationToken ct = default)
    {
        var order = await orders.GetByIdAsync(cmd.OrderId, ct);
        if (order is null) return false;

        order.Confirm();
        await orders.UpdateAsync(order, ct);

        var @event = order.DomainEvents.OfType<OrderConfirmedEvent>().Single();
        await outbox.EnqueueAsync(
            cmd.TenantId,
            $"confirm-{cmd.OrderId}",
            @event,
            new OutboxDestination { PublishAsNotification = true },
            cancellationToken: ct);

        order.ClearDomainEvents();
        await uow.SaveChangesAsync(ct);
        return true;
    }
}
