using MicroKit.Caching.Abstractions;
using MicroKit.Cqrs.MediatR.Handlers;
using OrderApi.Application.Orders.Dtos;
using OrderApi.Application.Orders.Queries;
using OrderApi.Domain.Orders.Repositories;

namespace OrderApi.Application.Orders.Handlers;

public sealed class GetOrderByIdHandler(
    IOrderRepository orders,
    ICacheService cache)
    : QueryHandler<GetOrderByIdQuery, OrderDto?>
{
    private static string CacheKey(Guid id) => $"order:{id}";

    public override async Task<OrderDto?> HandleAsync(GetOrderByIdQuery query, CancellationToken ct = default)
    {
        var cached = await cache.GetAsync<OrderDto>(CacheKey(query.OrderId), ct);
        if (cached is not null) return cached;

        var order = await orders.GetByIdAsync(query.OrderId, ct);
        if (order is null) return null;

        var dto = new OrderDto(
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
                i.UnitPrice.Currency)).ToList());

        await cache.SetAsync(CacheKey(query.OrderId), dto, new CacheOptions(TimeSpan.FromMinutes(5)), ct);
        return dto;
    }
}
