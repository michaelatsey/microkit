using MicroKit.Cqrs.MediatR.Handlers;
using OrderApi.Application.Orders.Dtos;
using OrderApi.Application.Orders.Queries;
using OrderApi.Domain.Orders.Repositories;

namespace OrderApi.Application.Orders.Handlers;

public sealed class ListOrdersByCustomerHandler(IOrderRepository orders)
    : QueryHandler<ListOrdersByCustomerQuery, IReadOnlyList<OrderDto>>
{
    public override async Task<IReadOnlyList<OrderDto>> HandleAsync(ListOrdersByCustomerQuery query, CancellationToken ct = default)
    {
        var results = await orders.GetByCustomerIdAsync(query.TenantId, query.CustomerId, ct);
        return results.Select(order => new OrderDto(
            order.Id,
            order.TenantId,
            order.CustomerId,
            order.Status.ToString(),
            order.TotalAmount.Amount,
            order.TotalAmount.Currency,
            order.PlacedAt,
            order.Items.Select(i => new OrderItemDto(
                i.ProductId,
                i.ProductName,
                i.Quantity,
                i.UnitPrice.Amount,
                i.UnitPrice.Currency)).ToList()))
            .ToList();
    }
}
