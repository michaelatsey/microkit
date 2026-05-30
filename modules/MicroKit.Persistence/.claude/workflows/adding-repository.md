# Workflow: Adding a Repository

Step-by-step guide for adding a new repository pair to MicroKit.Persistence.

## When to Use

When a new aggregate root needs persistence. A repository pair consists of:
1. `IXxxRepository` (write) in `MicroKit.Persistence.Abstractions`
2. `IXxxReadRepository` (read) in `MicroKit.Persistence.Abstractions`
3. `EfXxxRepository` (write impl) in `MicroKit.Persistence.EntityFrameworkCore`
4. `EfXxxReadRepository` (read impl) in `MicroKit.Persistence.EntityFrameworkCore`

## Steps

### 1. Confirm the Aggregate Is an IAggregateRoot

Only aggregate roots get their own repository. If the type is a value object or child entity,
it is persisted via its owning aggregate's repository.

### 2. Define the Contracts

```
/new-repository <AggregateName>
```

Or scaffold manually in `MicroKit.Persistence.Abstractions`:

```csharp
// ✅ Write contract
public interface IUserRepository : IRepository<User>
{
    ValueTask<User?> FindByEmailAsync(Email email, CancellationToken ct = default);
}

// ✅ Read contract
public interface IUserReadRepository : IReadRepository<User>
{
    ValueTask<UserSummaryDto?> GetSummaryAsync(UserId id, CancellationToken ct = default);
}
```

### 3. Add EF Core Implementations

In `MicroKit.Persistence.EntityFrameworkCore`:

```csharp
public sealed class EfUserRepository(AppDbContext ctx) : IUserRepository
{
    public async ValueTask<User?> FindAsync(UserId id, CancellationToken ct = default)
        => await ctx.Users.FindAsync([id.Value], ct).ConfigureAwait(false);

    public async ValueTask AddAsync(User user, CancellationToken ct = default)
        => await ctx.Users.AddAsync(user, ct).ConfigureAwait(false);

    // ... UpdateAsync, DeleteAsync, CommitAsync delegate to IUnitOfWork
    public async ValueTask CommitAsync(CancellationToken ct = default)
        => await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
}

public sealed class EfUserReadRepository(AppDbContext ctx) : IUserReadRepository
{
    public async ValueTask<IReadOnlyList<User>> ListAsync(QueryOptions<User> opts, CancellationToken ct = default)
    {
        var query = ctx.Users.AsNoTracking(); // ← mandatory first
        query = _evaluator.GetQuery(query, opts);
        return await query.ToListAsync(ct).ConfigureAwait(false);
    }
}
```

### 4. Add Entity Configuration

Create `Configurations/{AggregateName}Configuration.cs` in the EFCore project.

### 5. Register in DI

```csharp
services.AddScoped<IUserRepository, EfUserRepository>();
services.AddScoped<IUserReadRepository, EfUserReadRepository>();
```

### 6. Add Tests

```
/new-repository-tests UserRepository
```

The `test-generator` agent produces the mandatory test matrix using `InMemoryRepository<User>`.

### 7. Run checks

```bash
dotnet build && dotnet test modules/MicroKit.Persistence/MicroKit.Persistence.slnx
```

Invoke `dependency-guardian` on any `.csproj` changes.
