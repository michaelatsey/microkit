namespace MicroKit.Domain.Aggregates;

/// <summary>
/// Marks a class as a DDD aggregate root — the transactional consistency boundary
/// for a group of related domain objects persisted and loaded as a unit.
/// </summary>
/// <remarks>
/// Implement this interface (via <see cref="AggregateRoot{TId}"/>) to enable
/// the generic constraint on <c>IRepository&lt;TAggregate&gt;</c> in MicroKit.Persistence.
/// </remarks>
public interface IAggregateRoot;
