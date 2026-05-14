using MediatR;
using MicroKit.Cqrs.Abstractions.Queries;
using MicroKit.Resilience.Abstractions;
using OrderApi.Application.Orders.Dtos;

namespace OrderApi.Application.Orders.Queries;

public sealed record GetOrderByIdQuery(Guid OrderId, string TenantId)
    : IQuery<OrderDto?>, IRequest<OrderDto?>, IResilientRequest
{
    public string? PipelineName => null;
}
