

using MicroKit.Sample.OrderApi.Application.Abstractions;
using MicroKit.Sample.OrderApi.Domain.Entities;

namespace MicroKit.Sample.OrderApi.Application.CreateOrder
{
    public record CreateOrderCommand(long ProductId, decimal Amount) : ICommand<Order>;
}
