using MediatR;
using MicroKit.Cqrs.Abstractions.Commands;
using MicroKit.Idempotency.Abstractions.Contracts;
using MicroKit.Resilience.Abstractions;

namespace OrderApi.Application.Orders.Commands;

public sealed record ConfirmOrderCommand(Guid OrderId, string TenantId, string IdempotencyKey)
    : ICommand<bool>, IRequest<bool>, IIdempotentRequest<bool>, IResilientRequest
{
    public TimeSpan? IdempotencyExpiration => TimeSpan.FromHours(24);
    public string? PipelineName => null;
}
