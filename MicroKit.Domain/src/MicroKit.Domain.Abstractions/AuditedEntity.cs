using MicroKit.Domain.Contracts;

namespace MicroKit.Domain.Abstractions;

/// <summary>Base class for entities with audit fields (creator, creation time, last modification).</summary>
public abstract class AuditedEntity : IAuditedEntity
{
    /// <summary>Gets the UTC timestamp when this entity was created.</summary>
    public DateTimeOffset CreatedOnUtc { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the identifier of the user who created this entity.</summary>
    public string? CreatedBy { get; private set; }

    /// <summary>Gets the UTC timestamp of the most recent modification, or <see langword="null"/> if never modified.</summary>
    public DateTimeOffset? LastModifiedOnUtc { get; private set; }

    /// <summary>Gets the identifier of the user who last modified this entity.</summary>
    public string? LastModifiedBy { get; private set; }

    /// <summary>Sets all audit fields. Called by persistence interceptors — not for direct use.</summary>
    internal void SetAuditFields(
        DateTimeOffset createdOnUtc,
        string? createdBy,
        DateTimeOffset? lastModifiedOnUtc,
        string? lastModifiedBy)
    {
        CreatedOnUtc = createdOnUtc;
        CreatedBy = createdBy;
        LastModifiedOnUtc = lastModifiedOnUtc;
        LastModifiedBy = lastModifiedBy;
    }

    /// <summary>Initializes a new instance of <see cref="AuditedEntity"/>.</summary>
    protected AuditedEntity()
    {
    }
}

/// <summary>Base class for entities with a strongly-typed key and audit fields.</summary>
/// <typeparam name="TKey">The type of the primary key.</typeparam>
public abstract class AuditedEntity<TKey>
    : Entity<TKey>, IAuditedEntity
    where TKey : notnull
{
    /// <summary>Gets the UTC timestamp when this entity was created.</summary>
    public DateTimeOffset CreatedOnUtc { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the identifier of the user who created this entity.</summary>
    public string? CreatedBy { get; private set; }

    /// <summary>Gets the UTC timestamp of the most recent modification, or <see langword="null"/> if never modified.</summary>
    public DateTimeOffset? LastModifiedOnUtc { get; private set; }

    /// <summary>Gets the identifier of the user who last modified this entity.</summary>
    public string? LastModifiedBy { get; private set; }

    /// <summary>Sets all audit fields. Called by persistence interceptors — not for direct use.</summary>
    internal void SetAuditFields(
        DateTimeOffset createdOnUtc,
        string? createdBy,
        DateTimeOffset? lastModifiedOnUtc,
        string? lastModifiedBy)
    {
        CreatedOnUtc = createdOnUtc;
        CreatedBy = createdBy;
        LastModifiedOnUtc = lastModifiedOnUtc;
        LastModifiedBy = lastModifiedBy;
    }

    /// <summary>Initializes a new instance of <see cref="AuditedEntity{TKey}"/>.</summary>
    protected AuditedEntity()
    {
    }

    /// <summary>Initializes a new instance of <see cref="AuditedEntity{TKey}"/> with the given primary key.</summary>
    /// <param name="id">The primary key value.</param>
    protected AuditedEntity(TKey id)
        : base(id)
    {
    }
}
