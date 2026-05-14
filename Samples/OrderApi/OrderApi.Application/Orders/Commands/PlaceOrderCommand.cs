using MediatR;
using MicroKit.Cqrs.Abstractions.Commands;
using MicroKit.Idempotency.Abstractions.Contracts;
using MicroKit.Resilience.Abstractions;
using OrderApi.Application.Orders.Dtos;

namespace OrderApi.Application.Orders.Commands;

public sealed record PlaceOrderCommand(
    Guid OrderId,
    string TenantId,
    string CustomerId,
    IReadOnlyList<OrderItemDto> Items,
    string IdempotencyKey)
    : ICommand<Guid>, IRequest<Guid>, IIdempotentRequest<Guid>, IResilientRequest
{
    public TimeSpan? IdempotencyExpiration => TimeSpan.FromHours(24);
    public string? PipelineName => null;
}
