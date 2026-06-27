namespace MicroKit.Tenancy;

/// <summary>
/// Marker interface that declares an entity as tenant-scoped.
/// Implementing types MUST expose a non-nullable <see cref="TenantId"/> property (MKT001).
/// EF Core query filters and the <c>TenantStampInterceptor</c> are applied automatically
/// to all types implementing this interface.
/// </summary>
public interface ITenantEntity
{
    /// <summary>The tenant this entity belongs to. Must not be <see langword="null"/>.</summary>
    TenantId TenantId { get; }
}
