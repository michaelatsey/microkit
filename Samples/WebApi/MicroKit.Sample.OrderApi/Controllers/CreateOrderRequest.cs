namespace MicroKit.Sample.OrderApi.Controllers;

/// <summary>HTTP request body for creating a new order.</summary>
/// <param name="ProductId">The product identifier.</param>
/// <param name="Amount">The order total amount.</param>
public record CreateOrderRequest(long ProductId, decimal Amount)
{
}
