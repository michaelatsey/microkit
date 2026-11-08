# Template: Domain Specification

For a new `MicroKit.Domain.Specifications.Specification<T>` subclass, written in the consumer's
domain layer. A specification overrides `ToExpression()` — nothing else.

---

## Simple specification

```csharp
/// <summary>Matches <see cref="{Aggregate}"/> aggregates that are {condition}.</summary>
public sealed class {Condition}{Aggregate}Spec : Specification<{Aggregate}>
{
    /// <inheritdoc/>
    public override Expression<Func<{Aggregate}, bool>> ToExpression() => e => /* predicate */;
}
```

Example:

```csharp
public sealed class OpenOrdersSpec : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression() => order => !order.IsClosed;
}
```

---

## Parameterised specification

```csharp
/// <summary>Matches <see cref="{Aggregate}"/> aggregates whose {Property} equals <paramref name="{parameter}"/>.</summary>
public sealed class {Aggregate}By{Property}Spec({PropertyType} {parameter}) : Specification<{Aggregate}>
{
    /// <inheritdoc/>
    public override Expression<Func<{Aggregate}, bool>> ToExpression() => e => e.{Property} == {parameter};
}
```

---

## Tests

```csharp
public sealed class {Condition}{Aggregate}SpecTests
{
    [Fact]
    public void IsSatisfiedBy_When{MatchCondition}_ReturnsTrue()
    {
        var aggregate = /* a matching aggregate */;

        new {Condition}{Aggregate}Spec().IsSatisfiedBy(aggregate).ShouldBeTrue();
    }

    [Fact]
    public void IsSatisfiedBy_When{NoMatchCondition}_ReturnsFalse()
    {
        var aggregate = /* a non-matching aggregate */;

        new {Condition}{Aggregate}Spec().IsSatisfiedBy(aggregate).ShouldBeFalse();
    }
}
```

Add one SQLite integration test that runs the specification through `IReadRepository` to prove
EF Core translates it.

---

## Usage in a query handler

```csharp
var opts = new QueryOptions<{Aggregate}>(new {Condition}{Aggregate}Spec())
    .WithOrderBy(e => e.{SortKey})
    .WithPagination(query.Page, query.PageSize);

var page = await _readRepository.ListPagedAsync(opts, ct).ConfigureAwait(false);
```

Includes, ordering, pagination and tracking belong to `QueryOptions`, never to the specification.
