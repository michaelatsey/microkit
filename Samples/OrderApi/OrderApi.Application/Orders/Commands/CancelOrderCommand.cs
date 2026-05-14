using MediatR;
using MicroKit.Cqrs.Abstractions.Commands;

namespace OrderApi.Application.Orders.Commands;

public sealed record CancelOrderCommand(Guid OrderId, string TenantId)
    : ICommand<bool>, IRequest<bool>;
