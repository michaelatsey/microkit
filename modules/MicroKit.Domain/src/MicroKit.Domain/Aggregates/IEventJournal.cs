namespace MicroKit.Domain.Aggregates;

/// <summary>
/// The internal view <see cref="AuditRules"/> needs of an aggregate's domain events, to remove the
/// events a failed mutation raised. <see cref="AggregateRoot{TId}"/> implements it explicitly, so it
/// adds nothing to the public surface.
/// </summary>
internal interface IEventJournal
{
    /// <summary>
    /// Gets the number of domain events raised and not removed, drained or not.
    /// </summary>
    int RaisedEventCount { get; }

    /// <summary>
    /// Removes the domain events raised since <see cref="RaisedEventCount"/> read
    /// <paramref name="raisedEventCount"/>.
    /// </summary>
    /// <param name="raisedEventCount">The value of <see cref="RaisedEventCount"/> taken before the operation.</param>
    void RemoveEventsRaisedSince(int raisedEventCount);
}
