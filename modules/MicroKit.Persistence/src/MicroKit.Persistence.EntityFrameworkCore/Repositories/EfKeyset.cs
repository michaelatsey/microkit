using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// A keyset ordering resolved against the model: each sort key a non-nullable scalar property of the root, then the
/// root's primary key; the predicate that starts a page after a cursor; the cursor of a page's last row (ADR-013).
/// </summary>
/// <remarks>
/// <para>
/// The page starts after the cursor with <c>k1 &gt;= v1 AND ((k1 &gt; v1) OR (k1 = v1 AND ((k2 &gt; v2) OR …)))</c>,
/// <c>&lt;=</c> and <c>&lt;</c> for a descending key: the leading bound is a range an index on the keys can seek.
/// Each comparison is a binary node carrying a helper method, which EF Core translates to the SQL operator for every
/// key type, a value-converted id or a <c>bool</c> included (pinned by <c>ReadSideFoundationTests</c>). The values
/// come from a holder, so the query is parameterised.
/// </para>
/// <para>
/// The cursor carries provider values: each key value goes through its property's value converter, and must be of
/// a type <see cref="KeysetCursor"/> carries; its fingerprint covers the root's model name and the ordering.
/// </para>
/// <para>
/// A plan is built once per root entity type and ordering (the resolved property names and directions) and cached:
/// the keys, the fingerprint, the generic methods and the root-only row projection.
/// </para>
/// </remarks>
internal sealed class EfKeyset<TAggregate>
    where TAggregate : class, IAggregateRoot
{
    private static readonly ConditionalWeakTable<IEntityType, ConcurrentDictionary<string, EfKeyset<TAggregate>>> s_plans = new();

    private static readonly MethodInfo EfPropertyMethod = typeof(EF).GetMethod(nameof(EF.Property))!;

    private static readonly MethodInfo GreaterThanMethod = Helper(nameof(GreaterThan));
    private static readonly MethodInfo GreaterThanOrEqualMethod = Helper(nameof(GreaterThanOrEqual));
    private static readonly MethodInfo LessThanMethod = Helper(nameof(LessThan));
    private static readonly MethodInfo LessThanOrEqualMethod = Helper(nameof(LessThanOrEqual));

    // The generic definitions, taken from typed delegates: no reflection over Queryable's members.
    private static readonly MethodInfo OrderByMethod =
        new Func<IQueryable<object>, Expression<Func<object, object>>, IOrderedQueryable<object>>(Queryable.OrderBy).Method.GetGenericMethodDefinition();

    private static readonly MethodInfo OrderByDescendingMethod =
        new Func<IQueryable<object>, Expression<Func<object, object>>, IOrderedQueryable<object>>(Queryable.OrderByDescending).Method.GetGenericMethodDefinition();

    private static readonly MethodInfo ThenByMethod =
        new Func<IOrderedQueryable<object>, Expression<Func<object, object>>, IOrderedQueryable<object>>(Queryable.ThenBy).Method.GetGenericMethodDefinition();

    private static readonly MethodInfo ThenByDescendingMethod =
        new Func<IOrderedQueryable<object>, Expression<Func<object, object>>, IOrderedQueryable<object>>(Queryable.ThenByDescending).Method.GetGenericMethodDefinition();

    private static readonly FieldInfo ValuesField = typeof(ValuesHolder).GetField(nameof(ValuesHolder.Values))!;

    private readonly string _display;
    private readonly IReadOnlyList<Key> _keys;
    private readonly byte[] _fingerprint;
    private readonly ParameterExpression _entity = Expression.Parameter(typeof(TAggregate), "e");
    private readonly Expression<Func<TAggregate, KeysetRow<TAggregate>>> _rootRow;

    private EfKeyset(IEntityType root, IReadOnlyList<Key> keys)
    {
        _display = root.DisplayName();
        _keys = keys;
        _fingerprint = KeysetCursor.FingerprintOf(root.Name, [.. keys.Select(k => (k.Property.Name, k.Descending))]);
        _rootRow = RowOf<TAggregate>(_entity);
    }

    /// <summary>A resolved key, with the generic methods made for its CLR type.</summary>
    private sealed record Key(
        IProperty Property,
        bool Descending,
        ValueConverter? Converter,
        Type ProviderType,
        MethodInfo PropertyMethod,
        MethodInfo Beyond,
        MethodInfo Bound,
        MethodInfo Order);

    /// <summary>Gets the plan of <paramref name="keyset"/>'s ordering on <paramref name="root"/>, its primary key appended.</summary>
    /// <param name="root">The root entity type.</param>
    /// <param name="keyset">The keyset.</param>
    /// <param name="optionsName">The name of the options parameter, for the exceptions.</param>
    /// <exception cref="ArgumentException">A key is not a scalar property of the root, or is nullable.</exception>
    /// <exception cref="InvalidOperationException">A key is a primitive collection, or is stored as a type a cursor cannot carry.</exception>
    internal static EfKeyset<TAggregate> For(IEntityType root, KeysetPagination<TAggregate> keyset, string optionsName)
    {
        var properties = new List<(IProperty Property, bool Descending)>(keyset.Keys.Count + 1);
        foreach (var key in keyset.Keys)
            properties.Add((PropertyOf(root, key, optionsName), key.Descending));
        foreach (var property in KeyLookup.PrimaryKeyOf(root))
        {
            if (!properties.Exists(p => ReferenceEquals(p.Property, property)))
                properties.Add((property, false));
        }

        var signature = string.Join("|", properties.Select(p => p.Property.Name + (p.Descending ? " desc" : string.Empty)));
        return s_plans.GetValue(root, static _ => new ConcurrentDictionary<string, EfKeyset<TAggregate>>(StringComparer.Ordinal))
            .GetOrAdd(signature, static (_, state) => Build(state.root, state.properties, state.optionsName), (root, properties, optionsName));
    }

    private static EfKeyset<TAggregate> Build(IEntityType root, List<(IProperty Property, bool Descending)> properties, string optionsName)
    {
        var keys = new List<Key>(properties.Count);
        for (var i = 0; i < properties.Count; i++)
            keys.Add(Resolve(root, properties[i].Property, properties[i].Descending, first: i == 0, optionsName));
        return new EfKeyset<TAggregate>(root, keys);
    }

    private static IProperty PropertyOf(IEntityType root, KeysetKey key, string optionsName)
    {
        var parameter = key.Selector.Parameters[0];
        var name = key.Selector.Body switch
        {
            // Unwrapped: a member reached through an interface reads ((IFace)x).Member.
            MemberExpression { Expression: { } target, Member: var member } when Unwrap(target) == parameter => member.Name,
            MethodCallExpression { Method: { Name: nameof(EF.Property), DeclaringType: var type }, Arguments: [var target, ConstantExpression { Value: string named }] }
                when type == typeof(EF) && Unwrap(target) == parameter => named,
            _ => null,
        };

        return (name is null ? null : root.FindProperty(name))
            ?? throw new ArgumentException(
                $"The keyset sort key '{key.Selector}' is not a scalar property of '{root.DisplayName()}' mapped by the model. " +
                "Sort by a property of the root.",
                optionsName);
    }

    private static Expression Unwrap(Expression expression) =>
        expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs, Operand: var operand } ? Unwrap(operand) : expression;

    [UnconditionalSuppressMessage(
        "Trimming", "IL2060:MakeGenericMethod",
        Justification = "EF.Property<T>, the comparison helpers and Queryable's ordering operators, made for the root and a model " +
            "property type and translated by EF Core, which is not trim-safe itself (see IL2091 in the project file).")]
    [UnconditionalSuppressMessage(
        "AOT", "IL3050:MakeGenericMethod",
        Justification = "EF Core's query pipeline requires dynamic code; MicroKit.Persistence.EntityFrameworkCore is not AOT-compatible.")]
    private static Key Resolve(IEntityType root, IProperty property, bool descending, bool first, string optionsName)
    {
        if (property.IsNullable)
        {
            throw new ArgumentException(
                $"The keyset sort key '{root.DisplayName()}.{property.Name}' is nullable: a keyset ordering needs a value on every row. " +
                "Sort by a required property.",
                optionsName);
        }

        if (property.IsPrimitiveCollection)
        {
            throw new InvalidOperationException(
                $"The keyset sort key '{root.DisplayName()}.{property.Name}' is a primitive collection, which a keyset cannot order by. " +
                "Sort by a scalar property.");
        }

        var converter = property.GetTypeMapping().Converter;
        var stored = converter?.ProviderClrType ?? property.ClrType;
        var providerType = Nullable.GetUnderlyingType(stored) ?? stored;
        if (!KeysetCursor.IsSupported(providerType))
        {
            throw new InvalidOperationException(
                $"The keyset sort key '{root.DisplayName()}.{property.Name}' is stored as '{providerType.Name}', which a keyset cursor " +
                "cannot carry: a cursor carries integers, Guid, string, bool, decimal, double, float, DateTime, DateTimeOffset, " +
                "DateOnly, TimeOnly and byte[]. Sort by another property, or convert this one to one of these types.");
        }

        var type = property.ClrType;
        var order = (first, descending) switch
        {
            (true, false) => OrderByMethod,
            (true, true) => OrderByDescendingMethod,
            (false, false) => ThenByMethod,
            _ => ThenByDescendingMethod,
        };
        return new Key(
            property,
            descending,
            converter,
            providerType,
            EfPropertyMethod.MakeGenericMethod(type),
            (descending ? LessThanMethod : GreaterThanMethod).MakeGenericMethod(type),
            (descending ? LessThanOrEqualMethod : GreaterThanOrEqualMethod).MakeGenericMethod(type),
            order.MakeGenericMethod(typeof(TAggregate), type));
    }

    /// <summary>Starts <paramref name="query"/> after <paramref name="after"/>, when given, and orders it by the keys.</summary>
    /// <exception cref="ArgumentException">The cursor belongs to another aggregate type or ordering, or its values do not match the keys.</exception>
    internal IQueryable<TAggregate> Apply(IQueryable<TAggregate> query, KeysetCursor? after, string optionsName)
    {
        if (after is not null)
            query = query.Where(After(ModelValuesOf(after, optionsName)));

        var expression = query.Expression;
        foreach (var key in _keys)
            expression = Expression.Call(key.Order, expression, Expression.Quote(Expression.Lambda(Property(key), _entity)));
        return query.Provider.CreateQuery<TAggregate>(expression);
    }

    /// <summary>
    /// The projection of a page row: the keys' model values and the item — the root, or <paramref name="selector"/> —
    /// from the same row.
    /// </summary>
    internal Expression<Func<TAggregate, KeysetRow<TResult>>> Row<TResult>(Expression<Func<TAggregate, TResult>>? selector) =>
        selector is null
            ? (Expression<Func<TAggregate, KeysetRow<TResult>>>)(object)_rootRow
            : RowOf<TResult>(new ReplaceParameter(selector.Parameters[0], _entity).Visit(selector.Body));

    private Expression<Func<TAggregate, KeysetRow<TResult>>> RowOf<TResult>(Expression item)
    {
        var keys = Expression.NewArrayInit(typeof(object), _keys.Select(k => Expression.Convert(Property(k), typeof(object))));
        var constructor = typeof(KeysetRow<TResult>).GetConstructor([typeof(object[]), typeof(TResult)])!;
        return Expression.Lambda<Func<TAggregate, KeysetRow<TResult>>>(Expression.New(constructor, keys, item), _entity);
    }

    /// <summary>The cursor of a row whose keys have <paramref name="modelValues"/>.</summary>
    internal KeysetCursor CursorOf(object?[] modelValues)
    {
        var values = new object[_keys.Count];
        for (var i = 0; i < values.Length; i++)
        {
            var key = _keys[i];
            var value = key.Converter is { } converter ? converter.ConvertToProvider(modelValues[i]) : modelValues[i];
            values[i] = value ?? throw new InvalidOperationException(
                $"The keyset sort key '{_display}.{key.Property.Name}' read null on a row: the ordering cannot continue after it.");
        }

        return KeysetCursor.Create(_fingerprint, values);
    }

    private object?[] ModelValuesOf(KeysetCursor after, string optionsName)
    {
        if (!after.IsFor(_fingerprint))
        {
            throw new ArgumentException(
                $"The keyset cursor was issued for another aggregate type or another ordering than this one of '{_display}' " +
                $"({string.Join(", ", _keys.Select(k => k.Property.Name + (k.Descending ? " desc" : string.Empty)))}).",
                optionsName);
        }

        if (after.Values.Count != _keys.Count)
        {
            throw new ArgumentException(
                $"The keyset cursor carries {after.Values.Count} values; this ordering of '{_display}' has {_keys.Count} keys.",
                optionsName);
        }

        var values = new object?[_keys.Count];
        for (var i = 0; i < values.Length; i++)
        {
            var key = _keys[i];
            var value = after.Values[i];
            if (value.GetType() != key.ProviderType)
            {
                throw new ArgumentException(
                    $"The keyset cursor does not match the sort key '{_display}.{key.Property.Name}': it carries a '{value.GetType().Name}', " +
                    $"the key is stored as '{key.ProviderType.Name}'.",
                    optionsName);
            }

            values[i] = key.Converter is { } converter ? converter.ConvertFromProvider(value) : value;
        }

        return values;
    }

    // k1 >= v1 AND ((k1 > v1) OR (k1 = v1 AND ((k2 > v2) OR …))): the OR chain alone is no range an index can seek.
    private Expression<Func<TAggregate, bool>> After(object?[] values)
    {
        var holder = Expression.Field(Expression.Constant(new ValuesHolder(values)), ValuesField);
        Expression? chain = null;
        Expression? bound = null;
        for (var i = _keys.Count - 1; i >= 0; i--)
        {
            var key = _keys[i];
            var property = Property(key);
            var value = Expression.Convert(Expression.ArrayIndex(holder, Expression.Constant(i)), key.Property.ClrType);
            var beyond = Compare(key.Descending, strict: true, key.Beyond, property, value);
            chain = chain is null ? beyond : Expression.OrElse(beyond, Expression.AndAlso(KeyLookup.KeyEquals(property, value), chain));
            if (i == 0 && _keys.Count > 1)
                bound = Compare(key.Descending, strict: false, key.Bound, property, value);
        }

        return Expression.Lambda<Func<TAggregate, bool>>(bound is null ? chain! : Expression.AndAlso(bound, chain!), _entity);
    }

    private MethodCallExpression Property(Key key) => Expression.Call(key.PropertyMethod, _entity, Expression.Constant(key.Property.Name));

    // A binary node with a helper method: EF Core translates the operator, whatever the key type's C# operators.
    private static BinaryExpression Compare(bool descending, bool strict, MethodInfo helper, Expression left, Expression right) =>
        (descending, strict) switch
        {
            (false, true) => Expression.GreaterThan(left, right, liftToNull: false, helper),
            (false, false) => Expression.GreaterThanOrEqual(left, right, liftToNull: false, helper),
            (true, true) => Expression.LessThan(left, right, liftToNull: false, helper),
            _ => Expression.LessThanOrEqual(left, right, liftToNull: false, helper),
        };

    private static MethodInfo Helper(string name) =>
        typeof(EfKeyset<TAggregate>).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    private static bool GreaterThan<T>(T left, T right) => Comparer<T>.Default.Compare(left, right) > 0;

    private static bool GreaterThanOrEqual<T>(T left, T right) => Comparer<T>.Default.Compare(left, right) >= 0;

    private static bool LessThan<T>(T left, T right) => Comparer<T>.Default.Compare(left, right) < 0;

    private static bool LessThanOrEqual<T>(T left, T right) => Comparer<T>.Default.Compare(left, right) <= 0;

    private sealed class ValuesHolder(object?[] values)
    {
        public readonly object?[] Values = values;
    }

    private sealed class ReplaceParameter(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}

/// <summary>A page row: the sort keys' model values and the item read with them.</summary>
internal sealed class KeysetRow<TResult>(object?[] keys, TResult item)
{
    internal object?[] Keys { get; } = keys;

    internal TResult Item { get; } = item;
}
