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

// EfUnitOfWork<TContext> implements it (#196 D3–D5):
// - CommitAsync = SaveChangesAsync, always translating to PersistenceException: DbUpdateException,
//   and the InvalidOperationException(DbUpdateException) a non-retrying provider strategy raises
//   for a failure it classifies as transient.
// - ExecuteAsync joins an open Database.CurrentTransaction (no strategy, no begin/commit/rollback);
//   otherwise it runs begin → operation → commit inside the provider's execution strategy,
//   unwrapping PersistenceException(DbUpdateException) so the strategy can retry a transient one,
//   and surfaces PersistenceException once the strategy gives up.
// - Rollback runs with CancellationToken.None and never masks the in-flight exception.
// There is no BeginTransactionAsync API: transactions are internal to ExecuteAsync.
```

## Anti-Patterns

```csharp
// ❌ MKP004: DbContext in query handler
public sealed class GetUsersHandler(AppDbContext ctx) { }

// ❌ MKP001: SaveChanges in read repository
await _context.SaveChangesAsync(); // inside IReadRepository

// ❌ Missing AsNoTracking (no analyzer — review it)
_context.Users.Where(u => u.IsActive).ToListAsync(); // no AsNoTracking

// ❌ IQueryable<T> on public interface
ValueTask<IQueryable<User>> GetQueryableAsync(); // leaks EF into app layer

// ❌ Raw string SQL without parameterization
.FromSqlRaw($"WHERE Email = '{email}'"); // SQL injection

// ❌ Lazy loading in a read path
// Any navigation property accessed outside Include() in a read repo is a lazy load
```
