using MicroKit.Domain.Contracts;

namespace MicroKit.Domain.Abstractions;

/// <summary>Base class for entities with audit fields (creator, creation time, last modification).</summary>
public abstract class AuditedEntity : IAuditedEntity
{
    public DateTimeOffset CreatedOnUtc { get; private set; } = DateTimeOffset.UtcNow;
    public string? CreatedBy { get; private set; }
    public DateTimeOffset? LastModifiedOnUtc { get; private set; }
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
    public DateTimeOffset CreatedOnUtc { get; private set; } = DateTimeOffset.UtcNow;
    public string? CreatedBy { get; private set; }
    public DateTimeOffset? LastModifiedOnUtc { get; private set; }
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

    protected AuditedEntity()
    {
    }

    protected AuditedEntity(TKey id)
        : base(id)
    {
    }
}
