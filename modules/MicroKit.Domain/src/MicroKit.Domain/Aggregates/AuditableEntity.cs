using MicroKit.Domain.Identifiers;

namespace MicroKit.Domain.Aggregates;

/// <summary>
/// Base class for auditable domain entities with identity-based equality and audit tracking.
/// Extends the standard entity behavior with lightweight audit metadata.
/// </summary>
/// <typeparam name="TId">The strongly-typed identifier type</typeparam>
/// <remarks>
/// This class provides audit properties but does not automatically populate them.
/// The Application/Infrastructure layers are responsible for setting audit values
/// during persistence operations based on the current security context.
/// </remarks>
public abstract class AuditableEntity<TId> : Entity<TId>, IAuditableEntity
    where TId : IEntityId
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuditableEntity{TId}"/> class.
    /// </summary>
    /// <param name="id">The strongly-typed identifier for this entity.</param>
    protected AuditableEntity(TId id) : base(id)
    {
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets when this entity was created in UTC.
    /// </summary>
    public DateTimeOffset CreatedAt { get; protected init; }

    /// <summary>
    /// Gets when this entity was last updated in UTC.
    /// Null if the entity has never been updated since creation.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; protected set; }

    /// <summary>
    /// Gets the identifier of the user who created this entity.
    /// Null if the creator is unknown or not tracked.
    /// </summary>
    public string? CreatedBy { get; protected init; }

    /// <summary>
    /// Gets the identifier of the user who last updated this entity.
    /// Null if the last updater is unknown or not tracked.
    /// </summary>
    public string? UpdatedBy { get; protected set; }
}