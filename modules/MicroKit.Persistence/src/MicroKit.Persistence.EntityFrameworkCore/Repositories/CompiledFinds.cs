using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// The compiled queries of the repositories' hot paths — the find by id of each side — built once per model
/// (ADR-013).
/// </summary>
/// <remarks>
/// <para>
/// EF Core 10 binds a compiled query to the first model it runs with and throws
/// (<c>CompiledQueryDifferentModel</c>) on a context of another model: one context type over two providers, or a
/// model cache key with variants. Each delegate is therefore cached per <see cref="IModel"/>, in a
/// <see cref="ConditionalWeakTable{TKey, TValue}"/> that lets a discarded model go.
/// </para>
/// <para>
/// A compiled query still applies the model's query filters, and evaluates the <see cref="DbContext"/> members a
/// filter reads at each execution: a tenant filter reads the executing context's tenant.
/// </para>
/// </remarks>
internal static class CompiledFinds
{
    private static readonly MethodInfo EfPropertyMethod = typeof(EF).GetMethod(nameof(EF.Property))!;

    /// <summary>The key predicate <c>e =&gt; EF.Property&lt;TKey&gt;(e, key) == value</c>, over the parameter <paramref name="value"/>.</summary>
    [UnconditionalSuppressMessage(
        "Trimming", "IL2060:MakeGenericMethod",
        Justification = "EF.Property<T> carries no trimming annotation and is only ever translated by EF Core, which is not trim-safe itself (see IL2091 in the project file).")]
    internal static Expression<Func<TEntity, bool>> KeyPredicate<TEntity>(IProperty key, ParameterExpression value)
    {
        var entity = Expression.Parameter(typeof(TEntity), "e");
        var property = Expression.Call(EfPropertyMethod.MakeGenericMethod(key.ClrType), entity, Expression.Constant(key.Name));
        return Expression.Lambda<Func<TEntity, bool>>(KeyLookup.KeyEquals(property, value), entity);
    }
}

/// <summary>
/// <c>EfRepository.FindAsync(TId)</c>'s load, compiled per model: the whole aggregate, tracked, with the includes of
/// <see cref="AggregateIncludePlan"/> and its split decision, filtered on the primary key.
/// </summary>
internal static class CompiledAggregateLoad<TAggregate, TId, TContext>
    where TAggregate : class, IAggregateRoot
    where TContext : DbContext
{
    private static readonly ConditionalWeakTable<IModel, Func<TContext, TId, IAsyncEnumerable<TAggregate>>> s_loads = new();

    private static readonly MethodInfo IncludeMethod =
        new Func<IQueryable<TAggregate>, string, IQueryable<TAggregate>>(EntityFrameworkQueryableExtensions.Include).Method;

    private static readonly MethodInfo AsSplitQueryMethod =
        new Func<IQueryable<TAggregate>, IQueryable<TAggregate>>(RelationalQueryableExtensions.AsSplitQuery).Method;

    private static readonly MethodInfo WhereMethod =
        new Func<IQueryable<TAggregate>, Expression<Func<TAggregate, bool>>, IQueryable<TAggregate>>(Queryable.Where).Method;

    /// <summary>Gets the compiled load of <paramref name="context"/>'s model, whose root entity type is <paramref name="root"/>.</summary>
    internal static Func<TContext, TId, IAsyncEnumerable<TAggregate>> For(TContext context, IEntityType root) =>
        s_loads.TryGetValue(context.Model, out var load) ? load : Add(context.Model, root);

    // The miss, apart: the closure over the root is allocated once per model, never on a hit.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Func<TContext, TId, IAsyncEnumerable<TAggregate>> Add(IModel model, IEntityType root) =>
        s_loads.GetValue(model, _ => Compile(root));

    private static Func<TContext, TId, IAsyncEnumerable<TAggregate>> Compile(IEntityType root)
    {
        Expression<Func<TContext, IQueryable<TAggregate>>> source = c => c.Set<TAggregate>().AsTracking();
        var id = Expression.Parameter(typeof(TId), "id");
        var plan = AggregateIncludePlan.For(root);

        var body = source.Body;
        foreach (var path in plan.Paths)
            body = Expression.Call(IncludeMethod, body, Expression.Constant(path));
        if (plan.IsSplit)
            body = Expression.Call(AsSplitQueryMethod, body);
        var key = KeyLookup.PrimaryKeyOf(root)[0];
        body = Expression.Call(WhereMethod, body, Expression.Quote(CompiledFinds.KeyPredicate<TAggregate>(key, id)));

        return EF.CompileAsyncQuery<TContext, TId, TAggregate>(
            Expression.Lambda<Func<TContext, TId, IQueryable<TAggregate>>>(body, source.Parameters[0], id));
    }
}

/// <summary>
/// <c>EfReadRepository.FindAsync</c> for a single-property primary key, compiled per model: the root alone,
/// untracked. A composite key stays a dynamic query.
/// </summary>
internal abstract class CompiledReadFind<TAggregate, TContext>
    where TAggregate : class, IAggregateRoot
    where TContext : DbContext
{
    private static readonly ConditionalWeakTable<IModel, CompiledReadFind<TAggregate, TContext>> s_finds = new();

    /// <summary>Runs the find for <paramref name="key"/>, a value of the key's CLR type.</summary>
    internal abstract Task<TAggregate?> FindAsync(TContext context, object key, CancellationToken ct);

    /// <summary>Gets the compiled find of <paramref name="context"/>'s model, whose root entity type is <paramref name="root"/>.</summary>
    internal static CompiledReadFind<TAggregate, TContext> For(TContext context, IEntityType root) =>
        s_finds.TryGetValue(context.Model, out var find) ? find : Add(context.Model, root);

    // The miss, apart: the closure over the root is allocated once per model, never on a hit.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CompiledReadFind<TAggregate, TContext> Add(IModel model, IEntityType root) =>
        s_finds.GetValue(model, _ => Create(root));

    // The key's CLR type is known from the model only: the typed find is made for it once per model.
    [UnconditionalSuppressMessage(
        "Trimming", "IL2055:MakeGenericType",
        Justification = "The type arguments are the aggregate and its primary key's CLR type, both rooted by the EF Core model, which is not trim-safe itself (see IL2091 in the project file).")]
    [UnconditionalSuppressMessage(
        "Trimming", "IL2072:Activator",
        Justification = "TypedFind has one public constructor, kept by its use here; EF Core is not trim-safe itself.")]
    [UnconditionalSuppressMessage(
        "AOT", "IL3050:MakeGenericType",
        Justification = "EF Core's query pipeline requires dynamic code; MicroKit.Persistence.EntityFrameworkCore is not AOT-compatible.")]
    private static CompiledReadFind<TAggregate, TContext> Create(IEntityType root)
    {
        var key = KeyLookup.PrimaryKeyOf(root)[0];
        var type = typeof(TypedFind<>).MakeGenericType(typeof(TAggregate), typeof(TContext), key.ClrType);
        return (CompiledReadFind<TAggregate, TContext>)Activator.CreateInstance(type, key)!;
    }

    private sealed class TypedFind<TKey> : CompiledReadFind<TAggregate, TContext>
    {
        private static readonly MethodInfo FirstOrDefaultMethod =
            new Func<IQueryable<TAggregate>, Expression<Func<TAggregate, bool>>, TAggregate?>(Queryable.FirstOrDefault).Method;

        private readonly Func<TContext, TKey, CancellationToken, Task<TAggregate?>> _find;

        public TypedFind(IProperty key)
        {
            Expression<Func<TContext, IQueryable<TAggregate>>> source = c => c.Set<TAggregate>().AsNoTracking();
            var value = Expression.Parameter(typeof(TKey), "key");
            var ct = Expression.Parameter(typeof(CancellationToken), "ct");
            var body = Expression.Call(FirstOrDefaultMethod, source.Body, Expression.Quote(CompiledFinds.KeyPredicate<TAggregate>(key, value)));
            _find = EF.CompileAsyncQuery(
                Expression.Lambda<Func<TContext, TKey, CancellationToken, TAggregate?>>(body, source.Parameters[0], value, ct));
        }

        internal override Task<TAggregate?> FindAsync(TContext context, object key, CancellationToken ct) => _find(context, (TKey)key, ct);
    }
}
