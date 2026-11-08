using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using MicroKit.Domain.ValueObjects.Common;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Maps an <see cref="EntityId{T}"/> to its <see cref="Guid"/> value, and back through
/// <typeparamref name="TId"/>'s <c>(Guid)</c> constructor, whatever its accessibility.
/// </summary>
/// <typeparam name="TId">The identifier type.</typeparam>
internal sealed class EntityIdConverter<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] TId>()
    : ValueConverter<TId, Guid>(id => id.Value, s_fromGuid)
    where TId : EntityId<TId>
{
    // Built once per identifier type; EF Core creates a converter per mapped property.
    private static readonly Expression<Func<Guid, TId>> s_fromGuid = FromGuid();

    private static Expression<Func<Guid, TId>> FromGuid()
    {
        var constructor = IdentifierConstructor.Find(typeof(TId))
            ?? throw new InvalidOperationException(IdentifierConstructor.MissingMessage(typeof(TId)));
        var value = Expression.Parameter(typeof(Guid), "value");
        return Expression.Lambda<Func<Guid, TId>>(Expression.New(constructor, value), value);
    }
}

/// <summary>The <c>(Guid)</c> constructor an identifier type is read back through.</summary>
internal static class IdentifierConstructor
{
    internal static ConstructorInfo? Find(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
        Type identifierType) =>
        identifierType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [typeof(Guid)]);

    internal static string MissingMessage(Type identifierType) =>
        $"'{identifierType.FullName}' derives from EntityId<{identifierType.Name}> but declares no constructor " +
        "taking a single Guid, so a stored value cannot be read back. Add one — any accessibility.";
}

/// <summary>Maps a <see cref="GuidId"/> to its <see cref="Guid"/> value.</summary>
internal sealed class GuidIdConverter() : ValueConverter<GuidId, Guid>(id => id.Value, value => new GuidId(value));

/// <summary>
/// Maps an <see cref="Actor"/> to its identifier string, and back through
/// <see cref="Actor.Restore(string)"/>, which returns the built-in actors' shared instances.
/// </summary>
internal sealed class ActorConverter() : ValueConverter<Actor, string>(actor => actor.Value, value => Actor.Restore(value));

/// <summary>
/// Normalises an audit instant to offset zero in both directions, and rejects a stored
/// <c>default(DateTimeOffset)</c>: the rules the <c>Auditable*</c> constructors enforce.
/// </summary>
/// <remarks>
/// The provider direction also converts query parameters, so it only normalises: rejecting
/// <c>default</c> there would fail an unbounded filter such as <c>CreatedAt &gt;= DateTimeOffset.MinValue</c>.
/// The <c>Auditable*</c> constructors already keep <c>default</c> out of the entity.
/// </remarks>
internal sealed class UtcInstantConverter()
    : ValueConverter<DateTimeOffset, DateTimeOffset>(
        instant => UtcInstant.ToStore(instant),
        stored => UtcInstant.FromStore(stored));

/// <summary>The checks <see cref="UtcInstantConverter"/> runs; methods, because an expression tree cannot throw.</summary>
internal static class UtcInstant
{
    internal static DateTimeOffset ToStore(DateTimeOffset instant) => instant.ToUniversalTime();

    internal static DateTimeOffset FromStore(DateTimeOffset stored) =>
        stored == default
            ? throw new InvalidOperationException(
                "A stored audit instant is 0001-01-01: the row holds no valid instant. Correct the row; " +
                "the Auditable* types never produce this value.")
            : stored.ToUniversalTime();
}
