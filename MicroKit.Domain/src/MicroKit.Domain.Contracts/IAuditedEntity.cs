namespace MicroKit.Domain.Contracts;

/// <summary>
/// Represents an entity that is audited.
/// </summary>
public interface IAuditedEntity
{
    /// <summary>Gets the UTC timestamp when this entity was created.</summary>
    DateTimeOffset CreatedOnUtc { get; }

    /// <summary>Gets the identifier of the actor who created this entity.</summary>
    string? CreatedBy { get; }

    /// <summary>Gets the UTC timestamp of the last modification, or <c>null</c> if never modified.</summary>
    DateTimeOffset? LastModifiedOnUtc { get; }

    /// <summary>Gets the identifier of the actor who last modified this entity.</summary>
    string? LastModifiedBy { get; }
}
