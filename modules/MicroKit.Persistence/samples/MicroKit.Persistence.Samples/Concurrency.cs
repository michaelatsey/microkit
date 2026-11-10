namespace MicroKit.Persistence.Samples;

public sealed record OrderSummary(OrderId Id, string Number, long Version);

public sealed class AddOrderLineIfMatch(IOrderRepository orders, IUnitOfWork unitOfWork, TimeProvider clock)
{
    // README: "Optimistic concurrency"
    public async ValueTask<long> HandleAsync(OrderId id, long ifMatch, string sku, Actor by, CancellationToken ct = default)
    {
        var order = await orders.FindAsync(id, expectedVersion: ifMatch, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Order {id} does not exist.");

        order.AddLine(sku, 1, by, clock);
        await unitOfWork.CommitAsync(ct).ConfigureAwait(false); // UPDATE … WHERE AggregateVersion = @ifMatch

        return orders.GetVersion(order); // the new ETag
    }
}

public static class OrderSummaries
{
    // README: "Optimistic concurrency" — the version in a projection
    public static IQueryable<OrderSummary> Of(ShopDbContext context) =>
        context.Orders.AsNoTracking()
            .Select(o => new OrderSummary(o.Id, o.Number, EF.Property<long>(o, AggregateVersion.PropertyName)));
}
