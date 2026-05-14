namespace OrderApi.Application.Orders.Dtos;

public sealed record OrderDto(
    Guid Id,
    string TenantId,
    string CustomerId,
    string Status,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset PlacedAt,
    IReadOnlyList<OrderItemDto> Items);

public sealed record OrderItemDto(string ProductId, string ProductName, int Quantity, decimal UnitPrice, string Currency);
