namespace MicroKit.Domain.Contracts;

/// <summary>
/// Represents an entity that is audited.
/// </summary>
public interface IAuditedEntity
{
    /// <summary>
    /// Gets or sets the created on UTC.
    /// </summary>
    /// <value>
    /// The created on UTC.
    /// </value>
    DateTimeOffset CreatedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the created by.
    /// </summary>
    /// <value>
    /// The created by.
    /// </value>
    string? CreatedBy { get; set; }

    /// <summary>
    /// Gets or sets the last modified on UTC.
    /// </summary>
    /// <value>
    /// The last modified on UTC.
    /// </value>
    DateTimeOffset? LastModifiedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the last modified by.
    /// </summary>
    /// <value>
    /// The last modified by.
    /// </value>
    string? LastModifiedBy { get; set; }
}
