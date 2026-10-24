using System.Globalization;
using MicroKit.Domain.ValueObjects.Common;

namespace MicroKit.Domain.Aggregates;

/// <summary>
/// The audit rules shared by <see cref="AuditableEntity{TId}"/> and
/// <see cref="AuditableAggregateRoot{TId}"/>, held once so the two bases cannot drift apart.
/// </summary>
/// <remarks>
/// Internal on purpose: ADR-003 D5 refused a public audit component. The XML remarks of the rules
/// stay on the public members of the two bases.
/// </remarks>
internal static class AuditRules
{
    /// <summary>
    /// Validates the creation fields and returns the creation instant at offset zero.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="createdBy"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="createdAt"/> is <c>default(DateTimeOffset)</c>.
    /// </exception>
    internal static DateTimeOffset Creation(DateTimeOffset createdAt, Actor createdBy)
    {
        ArgumentNullException.ThrowIfNull(createdBy);

        return UtcInstant.Normalize(createdAt, nameof(createdAt));
    }

    /// <summary>
    /// Reads the creation instant from <paramref name="timeProvider"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is null.</exception>
    internal static DateTimeOffset Now(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return timeProvider.GetUtcNow();
    }

    /// <summary>
    /// Validates a modification before its mutation runs, and returns its instant at offset zero.
    /// </summary>
    /// <param name="at">The instant of the modification.</param>
    /// <param name="by">The actor performing it.</param>
    /// <param name="mutation">The state change it wraps.</param>
    /// <param name="createdAt">The creation instant of the audited object.</param>
    /// <param name="updatedAt">Its current modification instant, if it was ever modified.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="by"/> or <paramref name="mutation"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="at"/> is <c>default(DateTimeOffset)</c>, or precedes
    /// <paramref name="updatedAt"/>, or <paramref name="createdAt"/> when there is no update yet.
    /// </exception>
    internal static DateTimeOffset Modification(
        DateTimeOffset at, Actor by, Action mutation, DateTimeOffset createdAt, DateTimeOffset? updatedAt)
    {
        ArgumentNullException.ThrowIfNull(by);
        ArgumentNullException.ThrowIfNull(mutation);

        var instant = UtcInstant.Normalize(at, nameof(at));
        var bound = updatedAt ?? createdAt;
        if (instant < bound)
            throw new ArgumentOutOfRangeException(
                nameof(at), at,
                string.Create(CultureInfo.InvariantCulture,
                    $"A modification at {instant:O} cannot precede {bound:O}, the {(updatedAt is null ? "creation instant" : "previous modification")}."));

        return instant;
    }

    /// <summary>
    /// Whether a modification that has just succeeded becomes the recorded one.
    /// </summary>
    /// <param name="instant">The instant of the modification, validated before its mutation ran.</param>
    /// <param name="updatedAt">The modification instant recorded now, after the mutation ran.</param>
    /// <returns>
    /// <see langword="false"/> when a nested modification of the same object, run inside the
    /// mutation, already recorded a later instant: overwriting it would make the recorded instant
    /// decrease.
    /// </returns>
    internal static bool Supersedes(DateTimeOffset instant, DateTimeOffset? updatedAt) =>
        updatedAt is null || instant >= updatedAt.Value;
}
