using MicroKit.Domain.Identifiers;

namespace MicroKit.Domain.Aggregates;

/// <summary>
/// Base class for auditable aggregate roots in DDD.
/// Combines aggregate root functionality with lightweight audit tracking.
/// </summary>
/// <typeparam name="TId">The strongly-typed identifier type</typeparam>
/// <remarks>
/// This class provides audit properties but does not automatically populate them.
/// The Application/Infrastructure layers are responsible for setting audit values
/// during persistence operations based on the current security context and business rules.
/// </remarks>
public abstract class AuditableAggregateRoot<TId> : AggregateRoot<TId>, IAuditableEntity
    where TId : IEntityId
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuditableAggregateRoot{TId}"/> class.
    /// </summary>
    /// <param name="id">The strongly-typed identifier for this aggregate root.</param>
    protected AuditableAggregateRoot(TId id) : base(id)
    {
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets when this aggregate was created in UTC.
    /// </summary>
    public DateTimeOffset CreatedAt { get; protected init; }

    /// <summary>
    /// Gets when this aggregate was last updated in UTC.
    /// Null if the aggregate has never been updated since creation.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; protected set; }

    /// <summary>
    /// Gets the identifier of the user who created this aggregate.
    /// Null if the creator is unknown or not tracked.
    /// </summary>
    public string? CreatedBy { get; protected init; }

    /// <summary>
    /// Gets the identifier of the user who last updated this aggregate.
    /// Null if the last updater is unknown or not tracked.
    /// </summary>
    public string? UpdatedBy { get; protected set; }
}