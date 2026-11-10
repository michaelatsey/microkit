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

// README: "Projections"
public sealed record OrderListItem(OrderId Id, string Number, int Lines);

public sealed class ListOpenOrderSummaries(IReadRepository<Order> orders)
{
    public ValueTask<IReadOnlyList<OrderListItem>> HandleAsync(CancellationToken ct = default) =>
        orders.ListAsync(
            new QueryOptions<Order>(new OpenOrdersSpec()).WithOrderBy(o => o.Number),
            o => new OrderListItem(o.Id, o.Number, o.Lines.Count), // built by the database
            ct);
}

// README: "Keyset pagination"
public sealed record OrderListPage(IReadOnlyList<OrderListItem> Items, string? Next);

public sealed class BrowseOpenOrders(IReadRepository<Order> orders)
{
    // null: the token is malformed, and the HTTP layer answers 400.
    public async ValueTask<OrderListPage?> HandleAsync(string? after, CancellationToken ct = default)
    {
        KeysetCursor? cursor = null;
        if (after is not null && !KeysetCursor.TryParse(after, out cursor))
            return null;

        var opts = new QueryOptions<Order>(new OpenOrdersSpec())
            .WithKeyset(KeysetPagination<Order>.OrderByDescending(o => o.CreatedAt) with { PageSize = 20, After = cursor });
        var page = await orders.ListKeysetAsync(opts, o => new OrderListItem(o.Id, o.Number, o.Lines.Count), ct).ConfigureAwait(false);
        return new OrderListPage(page.Items, page.Next?.ToString());
    }
}
