# Workflow: Adding a Specification

A specification is a reusable domain predicate; the query handler wraps it in `QueryOptions`.
Template: `.claude-context/templates/specification-template.md`. Rule: `.claude/rules/specifications.md`.

## 1. Is it a domain concern?

A specification when the predicate expresses a business rule ("open", "overdue", "eligible") and
is reused. A one-off UI filter is a `Specification<T>` local to the handler's feature, or a typed
read repository method — never a `Where` on a raw `DbSet` in a handler.

## 2. Write it in the consumer's domain layer

```csharp
public sealed class OpenOrdersSpec : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression() => order => !order.IsClosed;
}

public sealed class OrderByNumberSpec(string number) : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression() => order => order.Number == number;
}
```

The predicate must translate to SQL (no method EF Core cannot translate) and also run in memory
through `IsSatisfiedBy` — the in-memory doubles use it.

## 3. Use it from the query handler

```csharp
public sealed class ListOpenOrders(IReadRepository<Order> orders)
{
    public ValueTask<IPagedResult<Order>> HandleAsync(int page, CancellationToken ct = default) =>
        orders.ListPagedAsync(
            new QueryOptions<Order>(new OpenOrdersSpec())
                .WithOrderByDescending(o => o.CreatedAt)
                .WithPagination(page, pageSize: 20),
            ct);
}
```

Includes, ordering, pagination and tracking belong to `QueryOptions`, never to the specification.

## 4. Shared option presets (optional)

Extensions over `QueryOptions<T>` in the consumer, built on `WithSpec` and the `With-` builders:

```csharp
public static QueryOptions<Order> OpenFirst(this QueryOptions<Order> opts) =>
    opts.WithSpec(new OpenOrdersSpec()).WithOrderByDescending(o => o.CreatedAt);
```

## 5. Tests

`IsSatisfiedBy` for a matching and a non-matching aggregate (unit), and one SQLite test proving
the expression translates.
