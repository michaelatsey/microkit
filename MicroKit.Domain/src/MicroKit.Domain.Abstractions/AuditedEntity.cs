using MicroKit.Domain.Contracts;

namespace MicroKit.Domain.Abstractions;

[Serializable]
public abstract class AuditedEntity : IAuditedEntity
{
    public DateTimeOffset CreatedOnUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTimeOffset? LastModifiedOnUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? LastModifiedBy { get; set; }
    protected AuditedEntity()
    {
    }
}

[Serializable]
public abstract class AuditedEntity<TKey>
    : Entity<TKey>, IAuditedEntity
    where TKey : notnull
{
    public DateTimeOffset CreatedOnUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTimeOffset? LastModifiedOnUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? LastModifiedBy { get; set; }
    protected AuditedEntity()
    {
    }
    protected AuditedEntity(TKey id)
        : base(id)
    {
    }
}
