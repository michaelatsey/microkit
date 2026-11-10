namespace MicroKit.Persistence.Samples;

// README: "The write side"
public interface IOrderRepository : IRepository<Order, OrderId>
{
    ValueTask<Order?> FindByNumberAsync(string number, CancellationToken ct = default);
}

public sealed class EfOrderRepository(ShopDbContext context)
    : EfRepository<Order, OrderId, ShopDbContext>(context), IOrderRepository
{
    public async ValueTask<Order?> FindByNumberAsync(string number, CancellationToken ct = default)
    {
        var orders = await LoadAggregatesAsync(o => o.Number == number, ct).ConfigureAwait(false);
        return orders.SingleOrDefault();
    }
}
