namespace MicroKit.Persistence.Samples;

// README: "The read side"
public sealed class OpenOrdersSpec : Specification<Order>
{
    public override System.Linq.Expressions.Expression<Func<Order, bool>> ToExpression() => order => !order.IsClosed;
}

public sealed class ListOpenOrders(IReadRepository<Order> orders)
{
    public ValueTask<IPagedResult<Order>> HandleAsync(int page, CancellationToken ct = default) =>
        orders.ListPagedAsync(
            new QueryOptions<Order>(new OpenOrdersSpec())
                .WithOrderByDescending(o => o.CreatedAt)
                .WithThenBy(o => o.Id) // a unique tie-breaker: stable page boundaries
                .WithPagination(page, pageSize: 20),
            ct);
}
