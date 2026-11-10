namespace MicroKit.Persistence.Analyzers.Tests.Stubs;

/// <summary>
/// Standalone stub documents for the query-shape analyzers (MKP006–MKP009), compiled beside the test code
/// through <c>TestState.Sources</c>. Each document carries its own <c>using</c> directives; the namespaces and
/// signatures match the metadata names the analyzers resolve (EF Core 10.0.9, MicroKit.Domain, MicroKit.Persistence).
/// </summary>
internal static class QueryStubs
{
    /// <summary>The usings a test document starts with.</summary>
    public const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.ComponentModel.DataAnnotations.Schema;
        using System.Linq;
        using System.Linq.Expressions;
        using System.Threading;
        using System.Threading.Tasks;
        using MicroKit.Domain.Aggregates;
        using MicroKit.Domain.Identifiers;
        using MicroKit.Persistence;
        using MicroKit.Persistence.Abstractions;
        using Microsoft.EntityFrameworkCore;
        using Shop;

        """;

    /// <summary>MicroKit.Domain, MicroKit.Persistence.Abstractions and the core read side.</summary>
    public const string MicroKit = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Linq.Expressions;
        using System.Threading;
        using System.Threading.Tasks;
        using MicroKit.Domain.Aggregates;
        using MicroKit.Domain.Identifiers;

        namespace MicroKit.Domain.Aggregates
        {
            public interface IAggregateRoot { }
        }

        namespace MicroKit.Domain.Identifiers
        {
            public interface IEntityId { }
        }

        namespace MicroKit.Persistence.Abstractions
        {
            public interface IRepository<TAggregate, TId>
                where TAggregate : class, IAggregateRoot
                where TId : IEntityId
            {
                ValueTask<TAggregate?> FindAsync(TId id, CancellationToken ct = default);
                ValueTask<TAggregate?> FindAsync(TId id, long expectedVersion, CancellationToken ct = default);
                long GetVersion(TAggregate aggregate);
                ValueTask AddAsync(TAggregate aggregate, CancellationToken ct = default);
                ValueTask DeleteAsync(TAggregate aggregate, CancellationToken ct = default);
            }

            public interface IUnitOfWork
            {
                ValueTask CommitAsync(CancellationToken ct = default);
            }
        }

        namespace MicroKit.Persistence
        {
            public sealed record QueryOptions<TAggregate>
                where TAggregate : class, IAggregateRoot
            {
                public QueryOptions() { }
                public QueryOptions(object? specification) { }
                public Func<IQueryable<TAggregate>, IQueryable<TAggregate>>? Includes { get; init; }
                public bool AsSplitQueryEnabled { get; init; }
                public bool IncludeDeleted { get; init; }
            }

            public static class QueryOptionsExtensions
            {
                public static QueryOptions<TAggregate> WithIncludes<TAggregate>(
                    this QueryOptions<TAggregate> opts, Func<IQueryable<TAggregate>, IQueryable<TAggregate>> includes)
                    where TAggregate : class, IAggregateRoot => opts with { Includes = includes };

                public static QueryOptions<TAggregate> AsSplitQuery<TAggregate>(this QueryOptions<TAggregate> opts)
                    where TAggregate : class, IAggregateRoot => opts with { AsSplitQueryEnabled = true };

                public static QueryOptions<TAggregate> WithDeleted<TAggregate>(this QueryOptions<TAggregate> opts)
                    where TAggregate : class, IAggregateRoot => opts with { IncludeDeleted = true };
            }

            public sealed class KeysetPage<T> { }

            public sealed class PagedResult<T> { }

            public interface IReadRepository<TAggregate>
                where TAggregate : class, IAggregateRoot
            {
                ValueTask<TAggregate?> FindAsync(object[] keyValues, CancellationToken ct = default);
                ValueTask<IReadOnlyList<TAggregate>> ListAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);
                ValueTask<IReadOnlyList<TResult>> ListAsync<TResult>(QueryOptions<TAggregate> opts, Expression<Func<TAggregate, TResult>> selector, CancellationToken ct = default);
                ValueTask<PagedResult<TAggregate>> ListPagedAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);
                ValueTask<KeysetPage<TAggregate>> ListKeysetAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);
                ValueTask<bool> AnyAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);
                ValueTask<int> CountAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);
            }
        }
        """;

    /// <summary>EF Core: the context, sets, query extensions and explicit loading (no relational extensions).</summary>
    public const string EntityFrameworkCore = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Linq;
        using System.Linq.Expressions;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore.ChangeTracking;
        using Microsoft.EntityFrameworkCore.Query;

        namespace Microsoft.EntityFrameworkCore
        {
            public abstract class DbContext
            {
                public virtual DbSet<TEntity> Set<TEntity>() where TEntity : class => throw null!;
                public virtual TEntity? Find<TEntity>(params object?[]? keyValues) where TEntity : class => throw null!;
                public virtual ValueTask<TEntity?> FindAsync<TEntity>(params object?[]? keyValues) where TEntity : class => throw null!;
                public virtual EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class => throw null!;
                public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
                public virtual ChangeTracker ChangeTracker => throw null!;
            }

            public abstract class DbSet<TEntity> : IQueryable<TEntity>, IAsyncEnumerable<TEntity>
                where TEntity : class
            {
                public virtual TEntity? Find(params object?[]? keyValues) => throw null!;
                public virtual ValueTask<TEntity?> FindAsync(params object?[]? keyValues) => throw null!;
                public Type ElementType => typeof(TEntity);
                public Expression Expression => throw null!;
                public IQueryProvider Provider => throw null!;
                public IEnumerator<TEntity> GetEnumerator() => throw null!;
                IEnumerator IEnumerable.GetEnumerator() => throw null!;
                public IAsyncEnumerator<TEntity> GetAsyncEnumerator(CancellationToken cancellationToken = default) => throw null!;
            }

            public static class EntityFrameworkQueryableExtensions
            {
                public static IIncludableQueryable<TEntity, TProperty> Include<TEntity, TProperty>(
                    this IQueryable<TEntity> source, Expression<Func<TEntity, TProperty>> navigationPropertyPath)
                    where TEntity : class => throw null!;

                public static IQueryable<TEntity> Include<TEntity>(this IQueryable<TEntity> source, string navigationPropertyPath)
                    where TEntity : class => throw null!;

                public static IIncludableQueryable<TEntity, TProperty> ThenInclude<TEntity, TPreviousProperty, TProperty>(
                    this IIncludableQueryable<TEntity, IEnumerable<TPreviousProperty>> source,
                    Expression<Func<TPreviousProperty, TProperty>> navigationPropertyPath)
                    where TEntity : class => throw null!;

                public static IIncludableQueryable<TEntity, TProperty> ThenInclude<TEntity, TPreviousProperty, TProperty>(
                    this IIncludableQueryable<TEntity, TPreviousProperty> source,
                    Expression<Func<TPreviousProperty, TProperty>> navigationPropertyPath)
                    where TEntity : class => throw null!;

                public static IQueryable<TEntity> IgnoreQueryFilters<TEntity>(this IQueryable<TEntity> source)
                    where TEntity : class => throw null!;

                public static IQueryable<TEntity> IgnoreQueryFilters<TEntity>(
                    this IQueryable<TEntity> source, IReadOnlyCollection<string> filterKeys)
                    where TEntity : class => throw null!;

                public static IQueryable<TEntity> AsNoTracking<TEntity>(this IQueryable<TEntity> source) where TEntity : class => throw null!;
                public static IQueryable<TEntity> AsTracking<TEntity>(this IQueryable<TEntity> source) where TEntity : class => throw null!;
                public static IQueryable<T> TagWith<T>(this IQueryable<T> source, string tag) => throw null!;

                public static Task<List<TSource>> ToListAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => throw null!;
                public static Task<TSource[]> ToArrayAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => throw null!;
                public static Task<TSource?> FirstOrDefaultAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => throw null!;
                public static Task<TSource?> FirstOrDefaultAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, CancellationToken cancellationToken = default) => throw null!;
                public static Task<TSource> SingleAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, CancellationToken cancellationToken = default) => throw null!;
                public static Task<bool> AnyAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => throw null!;
                public static Task<int> CountAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => throw null!;
                public static IAsyncEnumerable<TSource> AsAsyncEnumerable<TSource>(this IQueryable<TSource> source) => throw null!;
                public static void Load<TSource>(this IQueryable<TSource> source) => throw null!;
                public static Task LoadAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => throw null!;
                public static Task<int> ExecuteDeleteAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default) => throw null!;
            }
        }

        namespace Microsoft.EntityFrameworkCore.Query
        {
            public interface IIncludableQueryable<out TEntity, out TProperty> : IQueryable<TEntity> { }
        }

        namespace Microsoft.EntityFrameworkCore.ChangeTracking
        {
            public class ChangeTracker
            {
                public virtual IEnumerable<EntityEntry> Entries() => throw null!;
            }

            public class EntityEntry
            {
                public virtual object Entity => throw null!;
            }

            public class EntityEntry<TEntity> : EntityEntry
                where TEntity : class
            {
                public virtual CollectionEntry<TEntity, TProperty> Collection<TProperty>(
                    Expression<Func<TEntity, IEnumerable<TProperty>>> propertyExpression) where TProperty : class => throw null!;

                public virtual ReferenceEntry<TEntity, TProperty> Reference<TProperty>(
                    Expression<Func<TEntity, TProperty?>> propertyExpression) where TProperty : class => throw null!;
            }

            public abstract class NavigationEntry
            {
                public virtual void Load() { }
                public virtual Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            }

            public class CollectionEntry : NavigationEntry
            {
                public override void Load() { }
                public override Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            }

            public class CollectionEntry<TEntity, TRelatedEntity> : CollectionEntry where TEntity : class where TRelatedEntity : class { }

            public class ReferenceEntry : NavigationEntry
            {
                public override void Load() { }
                public override Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            }

            public class ReferenceEntry<TEntity, TProperty> : ReferenceEntry where TEntity : class where TProperty : class { }
        }
        """;

    /// <summary>EF Core relational: the split and single query hints.</summary>
    public const string Relational = """
        using System.Linq;

        namespace Microsoft.EntityFrameworkCore
        {
            public static class RelationalQueryableExtensions
            {
                public static IQueryable<TEntity> AsSplitQuery<TEntity>(this IQueryable<TEntity> source) where TEntity : class => throw null!;
                public static IQueryable<TEntity> AsSingleQuery<TEntity>(this IQueryable<TEntity> source) where TEntity : class => throw null!;
            }
        }
        """;

    /// <summary>A small shop model and its context, repositories and clock.</summary>
    public const string Shop = """
        using System;
        using System.Collections.Generic;
        using System.ComponentModel.DataAnnotations.Schema;
        using System.Threading;
        using System.Threading.Tasks;
        using MicroKit.Domain.Aggregates;
        using MicroKit.Domain.Identifiers;
        using MicroKit.Persistence;
        using MicroKit.Persistence.Abstractions;

        namespace Shop
        {
            public sealed record OrderId(Guid Value) : IEntityId;

            public enum OrderStatus { Open, Paid, Closed }

            public sealed class Customer
            {
                public string Name { get; set; } = "";
                public List<Address> Addresses { get; set; } = new();
            }

            public sealed class Address { public string City { get; set; } = ""; }

            public sealed class Product { public string Name { get; set; } = ""; }

            public sealed class Tax { public decimal Rate { get; set; } }

            public sealed class OrderLine
            {
                public Product Product { get; set; } = new();
                public List<Tax> Taxes { get; set; } = new();
                public int Quantity { get; set; }
            }

            public sealed class Payment { public decimal Amount { get; set; } }

            public sealed class Note { public string Text { get; set; } = ""; }

            public sealed class Order : IAggregateRoot
            {
                public OrderId Id { get; private set; } = new(Guid.Empty);
                public string Number { get; set; } = "";
                public decimal Total { get; private set; }
                public int Priority { get; init; }
                public DateTimeOffset PlacedAt { get; set; }
                public DateTimeOffset? ShippedAt { get; set; }
                public OrderStatus Status { get; set; }
                public Customer Customer { get; set; } = new();
                public Customer Shipper { get; set; } = new();
                internal int Rank { get; set; }
                public List<OrderLine> Lines { get; set; } = new();
                public List<Payment> Payments { get; set; } = new();
                public IEnumerable<Note> Notes { get; set; } = new List<Note>();
                [NotMapped] public string Label { get; set; } = "";
                public bool IsLarge => Total > 1000m;
                public bool IsLate(DateTimeOffset now) => PlacedAt < now;
            }

            public interface IOrderRepository : IRepository<Order, OrderId>
            {
                ValueTask<Order?> FindByNumberAsync(string number, CancellationToken ct = default);
                ValueTask AddRangeAsync(IEnumerable<Order> orders, CancellationToken ct = default);
                ValueTask RemoveExpiredAsync(CancellationToken ct = default);
            }

            public interface IOrderReadRepository : IReadRepository<Order> { }

            public interface IClock { DateTimeOffset GetUtcNow(); }
        }
        """;

    /// <summary>The shop's EF Core context.</summary>
    public const string ShopContext = """
        using Microsoft.EntityFrameworkCore;

        namespace Shop
        {
            public sealed class AppDbContext : DbContext
            {
                public DbSet<Order> Orders => Set<Order>();
            }
        }
        """;
}
