# Workflow: Adding a Specification

Step-by-step guide for adding a domain specification and its QueryOptions wrapper.

## When to Use

When a new query filter is needed that encapsulates domain logic (e.g., "active users",
"overdue orders", "products in a category").

## Steps

### 1. Determine if This Is a Domain Concern

A specification belongs in the **Domain** layer when:
- The criteria express business rules ("active", "overdue", "eligible")
- The predicate uses domain concepts (value objects, domain-level flags)

A raw filter belongs in the **query handler** (inline) when:
- It is a one-off UI filter with no business meaning
- It will never be reused across handlers

### 2. Create the Specification in Domain

```
/new-specification <SpecificationName>
```

File location: `modules/MicroKit.Domain/src/MicroKit.Domain/Specifications/`

```csharp
// ✅ Domain layer — criteria only
public sealed class ActiveUserSpec : Specification<User>
{
    public ActiveUserSpec() => AddCriteria(u => u.IsActive && !u.IsDeleted);
}

// ✅ Parameterised specification
public sealed class UserByEmailSpec : Specification<User>
{
    public UserByEmailSpec(Email email) => AddCriteria(u => u.Email == email);
}
```

### 3. Create the QueryOptions Wrapper in the Handler

QueryOptions is assembled in the **query handler** — it is an application-layer concern:

```csharp
public async ValueTask<Result<IReadOnlyList<UserDto>>> Handle(
    GetActiveUsersQuery query, CancellationToken ct = default)
{
    var opts = new QueryOptions<User>(new ActiveUserSpec())
        .WithIncludes(q => q.Include(u => u.Roles))
        .WithPagination(query.Page, query.PageSize)
        .AsNoTracking();

    var users = await _readRepo.ListAsync(opts, ct).ConfigureAwait(false);
    return Result.Success(users.Select(UserDto.From).ToList().AsReadOnly());
}
```

### 4. Add QueryOptions Extensions (if Needed)

If the spec needs sorting or complex composition used across many handlers, add helpers in
`MicroKit.Persistence.Specifications`:

```csharp
public static class UserQueryOptionsExtensions
{
    public static QueryOptions<User> ActiveUsers(this QueryOptions<User> opts)
        => opts.WithSpec(new ActiveUserSpec());
}
```

### 5. Write Specification Tests

```csharp
[Fact]
public void ActiveUserSpec_WhenActive_Matches()
{
    var user = User.CreateActive();
    var spec = new ActiveUserSpec();

    spec.Criteria.Compile()(user).ShouldBeTrue();
}

[Fact]
public void ActiveUserSpec_WhenDeleted_DoesNotMatch()
{
    var user = User.CreateDeleted();
    var spec = new ActiveUserSpec();

    spec.Criteria.Compile()(user).ShouldBeFalse();
}
```
