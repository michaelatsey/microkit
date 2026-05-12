

using MicroKit.Sample.OrderApi.Application.Abstractions;
using MicroKit.Sample.OrderApi.Domain.Entities;

namespace MicroKit.Sample.OrderApi.Application.CreateOrder
{
    /// <summary>Command to create a new order.</summary>
    /// <param name="ProductId">The product identifier.</param>
    /// <param name="Amount">The order total amount.</param>
    public record CreateOrderCommand(long ProductId, decimal Amount) : ICommand<Order>;
}
