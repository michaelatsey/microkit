# Template: Domain Specification

Use this template for new `Specification<T>` subclasses in MicroKit.Domain.

---

## Simple Specification

```csharp
namespace MicroKit.Domain.Specifications;

/// <summary>
/// Matches <see cref="{Entity}"/> aggregates that are {condition}.
/// </summary>
public sealed class {Condition}{Entity}Spec : Specification<{Entity}>
{
    public {Condition}{Entity}Spec()
        => AddCriteria(e => /* predicate */);
}
```

**Example:**
```csharp
public sealed class ActiveUserSpec : Specification<User>
{
    public ActiveUserSpec() => AddCriteria(u => u.IsActive && !u.IsDeleted);
}
```

---

## Parameterised Specification

```csharp
/// <summary>
/// Matches <see cref="{Entity}"/> aggregates where {property} equals <paramref name="{parameter}"/>.
/// </summary>
public sealed class {Entity}By{Property}Spec : Specification<{Entity}>
{
    public {Entity}By{Property}Spec({PropertyType} {parameter})
        => AddCriteria(e => e.{Property} == {parameter});
}
```

**Example:**
```csharp
public sealed class UserByEmailSpec : Specification<User>
{
    public UserByEmailSpec(Email email) => AddCriteria(u => u.Email == email);
}
```

---

## Test for the Specification

```csharp
public sealed class {Condition}{Entity}SpecTests
{
    [Fact]
    public void Criteria_When{MatchCondition}_Matches()
    {
        var entity = {Entity}.Create{MatchingFactory}();
        var spec = new {Condition}{Entity}Spec();

        spec.Criteria.Compile()(entity).ShouldBeTrue();
    }

    [Fact]
    public void Criteria_When{NoMatchCondition}_DoesNotMatch()
    {
        var entity = {Entity}.Create{NonMatchingFactory}();
        var spec = new {Condition}{Entity}Spec();

        spec.Criteria.Compile()(entity).ShouldBeFalse();
    }
}
```

---

## Usage in QueryHandler (application layer)

```csharp
// In the query handler — never in the spec itself
var opts = new QueryOptions<{Entity}>(new {Condition}{Entity}Spec())
    .WithPagination(query.Page, query.PageSize);

var results = await _readRepo.ListAsync(opts, ct).ConfigureAwait(false);
```
