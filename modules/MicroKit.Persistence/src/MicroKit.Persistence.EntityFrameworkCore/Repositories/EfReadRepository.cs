using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Generic EF Core read-side repository base implementing <see cref="IReadRepository{TAggregate}"/>.
/// Enforces <c>AsNoTracking()</c> on all queries; uses <see cref="ISpecificationEvaluator"/> for
/// all <see cref="QueryOptions{TAggregate}"/>-based transformations.
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must be a reference type implementing <see cref="IAggregateRoot"/>.
/// </typeparam>
/// <typeparam name="TContext">
/// The application <see cref="DbContext"/> type.
/// </typeparam>
/// <remarks>
/// <para>
/// Extend this class for typed read repositories:
/// <code>
/// public sealed class EfUserReadRepository(
///     AppDbContext ctx, ISpecificationEvaluator eval, PaginationLimits limits)
///     : EfReadRepository&lt;User, AppDbContext&gt;(ctx, eval, limits), IUserReadRepository
/// {
///     public async ValueTask&lt;UserSummaryDto?&gt; GetSummaryAsync(UserId id, CancellationToken ct = default)
///         => await Context.Users
///                .AsNoTracking()
///                .Where(u => u.Id == id)
///                .Select(UserSummaryDto.Projection)
///                .FirstOrDefaultAsync(ct)
///                .ConfigureAwait(false);
/// }
/// </code>
/// </para>
/// <para>
/// <see cref="FindAsync"/> returns an aggregate the unit of work already tracks untouched, and
/// loads any other one without tracking.
/// </para>
/// </remarks>
/// <param name="context">The scoped application <see cref="DbContext"/>.</param>
/// <param name="evaluator">Applies <see cref="QueryOptions{TAggregate}"/> to the queries.</param>
/// <param name="paginationLimits">
/// The default and maximum page sizes: the singleton <c>AddEntityFrameworkCore()</c> registers
/// (<see cref="PaginationLimits.Default"/>) or <c>ConfigurePagination(...)</c> replaced. Required,
/// so a typed subclass cannot silently fall back to the defaults and ignore the configuration.
/// </param>
public class EfReadRepository<TAggregate, TContext>(
    TContext context,
    ISpecificationEvaluator evaluator,
    PaginationLimits paginationLimits)
    : IReadRepository<TAggregate>
    where TAggregate : class, IAggregateRoot
    where TContext : DbContext
{
    private readonly PaginationLimits _paginationLimits =
        paginationLimits ?? throw new ArgumentNullException(nameof(paginationLimits));

    /// <summary>
    /// The underlying <typeparamref name="TContext"/> DbContext.
    /// Available to typed subclasses for aggregate-specific query methods.
    /// </summary>
    protected TContext Context { get; } = context;

    /// <summary>
    /// Finds an aggregate by its primary key.
    /// </summary>
    /// <param name="keyValues">
    /// The primary key values. Pass a single-element array for simple PKs;
    /// multiple elements for composite PKs, in the same order as the key definition.
    /// </param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The matching aggregate, or <see langword="null"/> if not found.</returns>
    /// <exception cref="ArgumentException">
    /// The number of <paramref name="keyValues"/> does not match the primary key of
    /// <typeparamref name="TAggregate"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The read and write repositories share the scoped <see cref="DbContext"/>. An aggregate the
    /// current unit of work already tracks is therefore returned <b>as is</b> — same instance, same
    /// state, <c>Added</c>, <c>Modified</c> or <c>Deleted</c> — so its pending change still reaches
    /// the next commit.
    /// </para>
    /// <para>
    /// Any other aggregate is loaded <b>without tracking</b> by a query on the primary key. It is
    /// never attached to the change tracker, so it is never wired into the navigations of entities
    /// the command tracks, and a later commit cannot try to write it.
    /// </para>
    /// <para>
    /// The tracked lookup runs with <see cref="ChangeTracker.AutoDetectChangesEnabled"/> turned
    /// off: reading <see cref="DbSet{TEntity}.Local"/> otherwise runs a full <c>DetectChanges</c>
    /// over every tracked entity. The caller's setting is restored afterwards.
    /// </para>
    /// </remarks>
    public async ValueTask<TAggregate?> FindAsync(object[] keyValues, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keyValues);
        var set = Context.Set<TAggregate>();
        var keyProperties = PrimaryKeyOf(set);
        if (keyValues.Length != keyProperties.Count)
        {
            throw new ArgumentException(
                $"'{typeof(TAggregate).Name}' has a primary key of {keyProperties.Count} value(s); {keyValues.Length} were given.",
                nameof(keyValues));
        }

        // A null key value matches nothing, as DbSet.Find answers.
        if (Array.Exists(keyValues, v => v is null))
            return null;

        var tracker = Context.ChangeTracker;
        var autoDetectChanges = tracker.AutoDetectChangesEnabled;
        EntityEntry<TAggregate>? tracked;
        try
        {
            tracker.AutoDetectChangesEnabled = false;
            tracked = set.Local.FindEntryUntyped(keyValues);
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = autoDetectChanges;
        }

        if (tracked is not null)
            return tracked.Entity;

        return await set.AsNoTracking()
            .FirstOrDefaultAsync(KeyPredicate(keyProperties, keyValues), ct)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<IProperty> PrimaryKeyOf(DbSet<TAggregate> set) =>
        set.EntityType.FindPrimaryKey()?.Properties
        ?? throw new InvalidOperationException($"'{typeof(TAggregate).Name}' has no primary key.");

    // e => EF.Property<T0>(e, "K0") == (T0)values[0] && ... . The values are read through a
    // closure object, so EF Core parameterises them and every call shares one compiled query.
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2060:MakeGenericMethod",
        Justification = "EF.Property<T> carries no trimming annotation and is only ever translated by EF Core, which is not trim-safe itself (see IL2091 in the project file).")]
    private static Expression<Func<TAggregate, bool>> KeyPredicate(
        IReadOnlyList<IProperty> keyProperties,
        object[] keyValues)
    {
        var entity = Expression.Parameter(typeof(TAggregate), "e");
        var values = Expression.Field(Expression.Constant(new KeyValuesHolder(keyValues)), KeyValuesField);

        Expression? body = null;
        for (var i = 0; i < keyProperties.Count; i++)
        {
            var clrType = keyProperties[i].ClrType;
            var property = Expression.Call(
                EfPropertyMethod.MakeGenericMethod(clrType),
                entity,
                Expression.Constant(keyProperties[i].Name));
            var value = Expression.Convert(
                Expression.ArrayIndex(values, Expression.Constant(i)),
                clrType);
            var equal = KeyEquals(property, value);
            body = body is null ? equal : Expression.AndAlso(body, equal);
        }

        return Expression.Lambda<Func<TAggregate, bool>>(body!, entity);
    }

    // == where the key type supports it (primitives, Guid, records…); otherwise value equality
    // through Equals(object), which EF Core translates to the same SQL comparison — DbSet.Find
    // accepts such keys, so the read repository must too. Probed once per key type.
    private static Expression KeyEquals(Expression property, Expression value)
    {
        var supportsEqualityOperator = s_equalityOperatorSupport.GetOrAdd(property.Type, static type =>
        {
            try
            {
                _ = Expression.Equal(Expression.Default(type), Expression.Default(type));
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        });

        return supportsEqualityOperator
            ? Expression.Equal(property, value)
            : Expression.Call(property, ObjectEqualsMethod, Expression.Convert(value, typeof(object)));
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> s_equalityOperatorSupport = new();

    private static readonly MethodInfo ObjectEqualsMethod =
        typeof(object).GetMethod(nameof(Equals), [typeof(object)])!;

    private static readonly MethodInfo EfPropertyMethod =
        typeof(EF).GetMethod(nameof(EF.Property))!;

    private static readonly FieldInfo KeyValuesField =
        typeof(KeyValuesHolder).GetField(nameof(KeyValuesHolder.Values))!;

    private sealed class KeyValuesHolder(object[] values)
    {
        public readonly object[] Values = values;
    }

    /// <summary>
    /// Returns all aggregates that satisfy the supplied <see cref="QueryOptions{TAggregate}"/>.
    /// </summary>
    /// <param name="opts">
    /// The query options specifying the filter, includes, ordering, and tracking hint.
    /// </param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>
    /// A read-only list of matching aggregates. Returns an empty list (never
    /// <see langword="null"/>) when no aggregates match.
    /// </returns>
    /// <remarks>
    /// Without <see cref="QueryOptions{TAggregate}.Pagination"/> every matching aggregate is
    /// returned; use <see cref="ListPagedAsync"/> for a bounded page.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The requested page size exceeds <see cref="PaginationLimits.MaxPageSize"/>.
    /// </exception>
    public async ValueTask<IReadOnlyList<TAggregate>> ListAsync(
        QueryOptions<TAggregate> opts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(opts);
        _paginationLimits.EnsureWithinMaximum(opts.Pagination);
        var query = evaluator.GetQuery(GetBaseQuery(opts), opts);
        return await query.ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a paginated list of aggregates that satisfy the supplied
    /// <see cref="QueryOptions{TAggregate}"/>.
    /// </summary>
    /// <param name="opts">
    /// The query options. When <see cref="QueryOptions{TAggregate}.Pagination"/> is not set, the
    /// first page of <see cref="PaginationLimits.DefaultPageSize"/> items is returned — never the
    /// whole result set.
    /// </param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>
    /// An <see cref="IPagedResult{TAggregate}"/> containing the current page of results
    /// and the total count across all pages.
    /// </returns>
    /// <remarks>
    /// Executes two database queries: one <c>COUNT</c> (without pagination, includes or ordering)
    /// and one <c>SELECT</c> (with <c>Skip/Take</c> applied).
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The requested page size exceeds <see cref="PaginationLimits.MaxPageSize"/>.
    /// </exception>
    public async ValueTask<IPagedResult<TAggregate>> ListPagedAsync(
        QueryOptions<TAggregate> opts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(opts);
        var pagination = _paginationLimits.ForPagedQuery(opts.Pagination);
        var baseQuery = GetBaseQuery(opts);

        var totalCount = await evaluator
            .GetQuery(baseQuery, AsCountQuery(opts))
            .CountAsync(ct).ConfigureAwait(false);

        var items = await evaluator
            .GetQuery(baseQuery, opts with { Pagination = pagination })
            .ToListAsync(ct).ConfigureAwait(false);

        return new PagedResult<TAggregate>(items, totalCount, pagination.Page, pagination.PageSize);
    }

    /// <summary>
    /// Returns <see langword="true"/> if at least one aggregate satisfies the supplied
    /// <see cref="QueryOptions{TAggregate}"/>.
    /// </summary>
    /// <param name="opts">The query options specifying the filter.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>
    /// <see langword="true"/> if at least one aggregate matches; <see langword="false"/> otherwise.
    /// </returns>
    /// <remarks>
    /// Prefer <see cref="AnyAsync"/> over <see cref="CountAsync"/> for existence checks —
    /// the database engine short-circuits after finding the first matching row.
    /// Includes, ordering, and pagination are stripped before execution.
    /// </remarks>
    public async ValueTask<bool> AnyAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(opts);
        return await evaluator
            .GetQuery(GetBaseQuery(opts), AsCountQuery(opts))
            .AnyAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the total number of aggregates that satisfy the supplied
    /// <see cref="QueryOptions{TAggregate}"/>.
    /// </summary>
    /// <param name="opts">The query options specifying the filter.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The count of matching aggregates.</returns>
    /// <remarks>
    /// Includes, ordering, and pagination are stripped before execution.
    /// </remarks>
    public async ValueTask<int> CountAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(opts);
        return await evaluator
            .GetQuery(GetBaseQuery(opts), AsCountQuery(opts))
            .CountAsync(ct).ConfigureAwait(false);
    }

    private IQueryable<TAggregate> GetBaseQuery(QueryOptions<TAggregate> opts)
        => opts.AsNoTrackingEnabled
            ? Context.Set<TAggregate>().AsNoTracking()
            : Context.Set<TAggregate>().AsQueryable();

    private static QueryOptions<TAggregate> AsCountQuery(QueryOptions<TAggregate> opts)
        => opts with { Includes = null, OrderBy = null, Pagination = null, AsSplitQueryEnabled = false };
}
