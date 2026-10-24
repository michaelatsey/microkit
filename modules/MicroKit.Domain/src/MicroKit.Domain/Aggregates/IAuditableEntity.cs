using MicroKit.Domain.ValueObjects.Common;

namespace MicroKit.Domain.Aggregates;

/// <summary>
/// Marker interface for entities that support audit tracking.
/// Provides lightweight audit metadata without infrastructure dependencies.
/// </summary>
/// <remarks>
/// This interface defines the contract for audit properties but does not specify
/// how these properties are populated. The Application/Infrastructure layers are
/// responsible for setting audit values during persistence operations.
/// </remarks>
public interface IAuditableEntity
{
    /// <summary>
    /// Gets when this entity was created in UTC.
    /// </summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Gets the actor that created this entity.
    /// Never <see langword="null"/> — use <see cref="Actor.System"/> for actions with
    /// no user in context.
    /// </summary>
    Actor CreatedBy { get; }

    /// <summary>
    /// Gets when this entity was last updated in UTC.
    /// Null if the entity has never been updated since creation.
    /// </summary>
    DateTimeOffset? UpdatedAt { get; }

    /// <summary>
    /// Gets the actor that last updated this entity.
    /// <see langword="null"/> when the entity has never been updated since creation.
    /// </summary>
    Actor? UpdatedBy { get; }
}
