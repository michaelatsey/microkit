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
    /// Gets when this entity was last updated in UTC.
    /// Null if the entity has never been updated since creation.
    /// </summary>
    DateTimeOffset? UpdatedAt { get; }

    /// <summary>
    /// Gets the identifier of the user who created this entity.
    /// Null if the creator is unknown or not tracked.
    /// </summary>
    string? CreatedBy { get; }

    /// <summary>
    /// Gets the identifier of the user who last updated this entity.
    /// Null if the last updater is unknown or not tracked.
    /// </summary>
    string? UpdatedBy { get; }
}