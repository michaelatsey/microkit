using MicroKit.Sample.OrderApi.Application.Abstractions;
using MicroKit.Sample.OrderApi.Domain.Events;

namespace MicroKit.Sample.OrderApi.Application.CreateOrder;

/// <summary>Decrements inventory when an order is created.</summary>
public class UpdateStockOrderHandler : IDomainEventHandler<OrderCreatedEvent>
{
    /// <inheritdoc/>
    public async Task Handle(OrderCreatedEvent notification, CancellationToken ct)
    {
        // Logique de décrémentation du stock
        Console.WriteLine($"[STOCK] Inventaire mis à jour pour la commande {notification.OrderId}");
    }
}
