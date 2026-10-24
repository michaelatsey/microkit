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
    /// Runs one audited modification, the body of <c>Mutate</c> on both auditable bases.
    /// </summary>
    /// <param name="at">The instant of the modification.</param>
    /// <param name="by">The actor performing it.</param>
    /// <param name="mutation">The state change it wraps.</param>
    /// <param name="createdAt">The creation instant of the audited object.</param>
    /// <param name="updatedAt">The audited object's modification-instant field.</param>
    /// <param name="updatedBy">The audited object's modifying-actor field.</param>
    /// <param name="modifications">
    /// The audited object's count of recorded modifications, which tells a nested modification
    /// apart from an earlier one even when both carry the same instant and the same actor.
    /// </param>
    /// <param name="journal">The aggregate's events, or <see langword="null"/> for an entity.</param>
    /// <remarks>
    /// Validates before anything changes; runs <paramref name="mutation"/>; if it throws, restores
    /// both audit fields to their values before it ran and removes the events it raised, then
    /// rethrows; otherwise records the modification, unless a nested one already recorded a stamp at
    /// or after its instant. The fields are written through <see langword="ref"/>, so a nested modification of
    /// the same object writes the same storage this call reads back.
    /// </remarks>
    internal static void Mutate(
        DateTimeOffset at,
        Actor by,
        Action mutation,
        DateTimeOffset createdAt,
        ref DateTimeOffset? updatedAt,
        ref Actor? updatedBy,
        ref int modifications,
        IEventJournal? journal)
    {
        var instant = Modification(at, by, mutation, createdAt, updatedAt);

        var (previousAt, previousBy, previousModifications) = (updatedAt, updatedBy, modifications);
        var raisedEventCount = journal?.RaisedEventCount ?? 0;
        try
        {
            mutation();
        }
        catch
        {
            (updatedAt, updatedBy, modifications) = (previousAt, previousBy, previousModifications);
            journal?.RemoveEventsRaisedSince(raisedEventCount);
            throw;
        }

        var nestedRecorded = modifications != previousModifications;
        if (Supersedes(instant, updatedAt, nestedRecorded))
        {
            updatedAt = instant;
            updatedBy = by;
            modifications++;
        }
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
    private static DateTimeOffset Modification(
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
    /// <param name="nestedRecorded">Whether a nested modification of the same object recorded a stamp during the mutation.</param>
    /// <returns>
    /// <see langword="false"/> when a nested modification recorded a stamp at or after
    /// <paramref name="instant"/>: overwriting a later one would make the recorded instant decrease,
    /// and overwriting an equal one would credit the wrong actor with the last change.
    /// </returns>
    private static bool Supersedes(DateTimeOffset instant, DateTimeOffset? updatedAt, bool nestedRecorded) =>
        !nestedRecorded || instant > updatedAt!.Value;
}
