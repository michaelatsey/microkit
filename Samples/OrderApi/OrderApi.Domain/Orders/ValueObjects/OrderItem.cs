namespace OrderApi.Domain.Orders.ValueObjects;

public sealed record OrderItem(string ProductId, string ProductName, int Quantity, Money UnitPrice)
{
    public Money Subtotal => UnitPrice * Quantity;
}
