# Rule: EF Core Patterns — MicroKit.Persistence

## Always active for files in EntityFrameworkCore, PostgreSql, and SqlServer projects.

## Entity Configuration

```csharp
// ✅ Always IEntityTypeConfiguration<T> — never OnModelCreating sprawl
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        // Value object owned entity
        builder.OwnsOne(u => u.Email, eb =>
        {
            eb.Property(e => e.Value)
              .HasColumnName("Email")
              .IsRequired()
              .HasMaxLength(320);
        });

        // Value converter for strongly-typed IDs
        builder.Property(u => u.Id)
               .HasConversion(id => id.Value, value => new UserId(value));
    }
}

// ❌ Inline configuration in OnModelCreating
protected override void OnModelCreating(ModelBuilder builder)
{
    builder.Entity<User>().ToTable("Users"); // ❌ — move to IEntityTypeConfiguration
}
```

## AsNoTracking — Enforcement

```csharp
// ✅ REQUIRED on every IReadRepository implementation
public async ValueTask<IReadOnlyList<T>> ListAsync(QueryOptions<T> opts, CancellationToken ct = default)
{
    var query = _context.Set<T>()
        .AsNoTracking()                    // ← line 1, mandatory
        .Where(opts.Specification.Criteria);
    // ...
}

// ✅ Global query tracking behavior for read DbContexts (optional pattern)
optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTrackingWithIdentityResolution);
```

## EfSpecificationEvaluator

```csharp
// ✅ All query transformations flow through the evaluator — never inline
public sealed class EfSpecificationEvaluator : ISpecificationEvaluator
{
    public IQueryable<T> GetQuery<T>(IQueryable<T> inputQuery, QueryOptions<T> opts)
        where T : class, IAggregateRoot
    {
        var query = inputQuery;

        if (opts.Specification is { } spec)
            query = query.Where(spec.Criteria);

        if (opts.Includes is not null)
            query = opts.Includes(query);

        if (opts.AsSplitQueryEnabled)
            query = query.AsSplitQuery();

        if (opts.Pagination is { } p)
            query = query.Skip(p.Skip).Take(p.PageSize);  // p.Skip = (p.Page - 1) * p.PageSize

        return query;
    }
}
```

## Split Query vs Single Query

```csharp
// ❌ Cartesian explosion — 2 levels of collections in one query
var users = context.Users
    .Include(u => u.Orders)
    .ThenInclude(o => o.Lines)
    .ToList(); // potentially millions of rows

// ✅ AsSplitQuery for multi-collection includes
var opts = new QueryOptions<User>(spec)
    .WithIncludes(q => q.Include(u => u.Orders).ThenInclude(o => o.Lines))
    .AsSplitQuery(); // 3 SELECT statements, no Cartesian product
```

## ITransactionalUnitOfWork

```csharp
// ✅ Composite interface — EF-specific, not in Abstractions
public interface ITransactionalUnitOfWork : IUnitOfWork, ITransactionalContext { }

public sealed class EfUnitOfWork(AppDbContext ctx) : ITransactionalUnitOfWork
{
    private IDbContextTransaction? _currentTransaction;

    public async ValueTask CommitAsync(CancellationToken ct = default)
    {
        try
        {
            await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new PersistenceException("Concurrency conflict during commit.", ex);
        }
        catch (DbUpdateException ex)
        {
            throw new PersistenceException("Database error during commit.", ex);
        }
    }

    public async ValueTask<ITransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        _currentTransaction = await ctx.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        return new EfTransaction(_currentTransaction);
    }

    public async ValueTask CommitTransactionAsync(CancellationToken ct = default)
    {
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        await _currentTransaction!.CommitAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_currentTransaction is not null)
            await _currentTransaction.RollbackAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_currentTransaction is not null)
            await _currentTransaction.DisposeAsync().ConfigureAwait(false);
    }
}
```

## Anti-Patterns

```csharp
// ❌ PRDANA001: DbContext in query handler
public sealed class GetUsersHandler(AppDbContext ctx) { }

// ❌ PRDANA002: SaveChanges in read repository
await _context.SaveChangesAsync(); // inside IReadRepository

// ❌ PRDANA003: Missing AsNoTracking
_context.Users.Where(u => u.IsActive).ToListAsync(); // no AsNoTracking

// ❌ IQueryable<T> on public interface
ValueTask<IQueryable<User>> GetQueryableAsync(); // leaks EF into app layer

// ❌ Raw string SQL without parameterization
.FromSqlRaw($"WHERE Email = '{email}'"); // SQL injection

// ❌ Lazy loading in a read path
// Any navigation property accessed outside Include() in a read repo is a lazy load
```
