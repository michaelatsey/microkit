using MediatR;
using MicroKit.Cqrs.Abstractions.Queries;
using OrderApi.Application.Orders.Dtos;

namespace OrderApi.Application.Orders.Queries;

public sealed record ListOrdersByCustomerQuery(string TenantId, string CustomerId)
    : IQuery<IReadOnlyList<OrderDto>>, IRequest<IReadOnlyList<OrderDto>>;
