namespace MicroKit.Sample.OrderApi.Controllers;

public record CreateOrderRequest(long ProductId, decimal Amount)
{
}
