using MicroKit.Domain.Contracts;

namespace MicroKit.Domain.Abstractions;

/// <summary>
/// Represents an aggregate root that is audited.
/// </summary>
/// <seealso cref="IAuditedEntity" />
[Serializable]
public abstract class AuditedAggregateRoot : AggregateRoot, IAuditedEntity
{
    /// <summary>
    /// Gets or sets the created on UTC.
    /// </summary>
    /// <value>
    /// The created on UTC.
    /// </value>
    public DateTimeOffset CreatedOnUtc { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or sets the created by.
    /// </summary>
    /// <value>
    /// The created by.
    /// </value>
    public string? CreatedBy { get; set; }
    /// <summary>
    /// Gets or sets the last modified on UTC.
    /// </summary>
    /// <value>
    /// The last modified on UTC.
    /// </value>
    public DateTimeOffset? LastModifiedOnUtc { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or sets the last modified by.
    /// </summary>
    /// <value>
    /// The last modified by.
    /// </value>
    public string? LastModifiedBy { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditedAggregateRoot"/> class.
    /// </summary>
    protected AuditedAggregateRoot()
     : base()
    {
    }

    /// <summary>
    /// Updates the timestamp.
    /// </summary>
    protected void UpdateTimestamp()
    {
        LastModifiedOnUtc = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Represents an aggregate root that is audited.
/// </summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <seealso cref="IAuditedEntity" />
[Serializable]
public abstract class AuditedAggregateRoot<TKey>
    : AggregateRoot<TKey>, IAuditedEntity
    where TKey : notnull
{
    /// <summary>
    /// Gets or sets the created on UTC.
    /// </summary>
    /// <value>
    /// The created on UTC.
    /// </value>
    public DateTimeOffset CreatedOnUtc { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or sets the created by.
    /// </summary>
    /// <value>
    /// The created by.
    /// </value>
    public string? CreatedBy { get; set; }
    /// <summary>
    /// Gets or sets the last modified on UTC.
    /// </summary>
    /// <value>
    /// The last modified on UTC.
    /// </value>
    public DateTimeOffset? LastModifiedOnUtc { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or sets the last modified by.
    /// </summary>
    /// <value>
    /// The last modified by.
    /// </value>
    public string? LastModifiedBy { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditedAggregateRoot{TKey}"/> class.
    /// </summary>
    protected AuditedAggregateRoot()
    {
    }
    /// <summary>
    /// Initializes a new instance of the <see cref="AuditedAggregateRoot{TKey}"/> class.
    /// </summary>
    /// <param name="id">The identifier.</param>
    protected AuditedAggregateRoot(TKey id)
        : base(id)
    {
    }

    /// <summary>
    /// Updates the timestamp.
    /// </summary>
    protected void UpdateTimestamp(DateTimeOffset? date = null)
    {
        LastModifiedOnUtc = date ?? DateTimeOffset.UtcNow;
    }

}
