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
    public DateTimeOffset CreatedOnUtc { get; private set; } = DateTimeOffset.UtcNow;
    /// <summary>Gets the identifier of the actor who created this aggregate.</summary>
    public string? CreatedBy { get; private set; }
    /// <summary>Gets the UTC timestamp of the last modification, or <c>null</c> if never modified.</summary>
    public DateTimeOffset? LastModifiedOnUtc { get; private set; }
    /// <summary>Gets the identifier of the actor who last modified this aggregate.</summary>
    public string? LastModifiedBy { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditedAggregateRoot"/> class.
    /// </summary>
    protected AuditedAggregateRoot()
     : base()
    {
    }

    /// <summary>Records a modification timestamp and optional actor. Call from domain methods that change state.</summary>
    protected void UpdateTimestamp(string? modifiedBy = null)
    {
        LastModifiedOnUtc = DateTimeOffset.UtcNow;
        LastModifiedBy = modifiedBy;
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
    public DateTimeOffset CreatedOnUtc { get; private set; } = DateTimeOffset.UtcNow;
    /// <summary>Gets the identifier of the actor who created this aggregate.</summary>
    public string? CreatedBy { get; private set; }
    /// <summary>Gets the UTC timestamp of the last modification, or <c>null</c> if never modified.</summary>
    public DateTimeOffset? LastModifiedOnUtc { get; private set; }
    /// <summary>Gets the identifier of the actor who last modified this aggregate.</summary>
    public string? LastModifiedBy { get; private set; }

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

    /// <summary>Records a modification timestamp and optional actor. Call from domain methods that change state.</summary>
    protected void UpdateTimestamp(string? modifiedBy = null)
    {
        LastModifiedOnUtc = DateTimeOffset.UtcNow;
        LastModifiedBy = modifiedBy;
    }

}
