using MicroKit.Cqrs.MediatR.Handlers;
using OrderApi.Application.Orders.Commands;
using OrderApi.Domain;
using OrderApi.Domain.Orders.Repositories;

namespace OrderApi.Application.Orders.Handlers;

public sealed class CancelOrderHandler(
    IOrderRepository orders,
    IUnitOfWork uow)
    : CommandHandler<CancelOrderCommand, bool>
{
    public override async Task<bool> HandleAsync(CancelOrderCommand cmd, CancellationToken ct = default)
    {
        var order = await orders.GetByIdAsync(cmd.OrderId, ct);
        if (order is null) return false;

        order.Cancel();
        await orders.UpdateAsync(order, ct);
        await uow.SaveChangesAsync(ct);
        return true;
    }
}
