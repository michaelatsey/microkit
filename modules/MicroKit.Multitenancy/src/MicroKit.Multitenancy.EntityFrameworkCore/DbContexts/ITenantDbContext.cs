namespace MicroKit.Multitenancy.EntityFrameworkCore;

/// <summary>
/// Marker interface for <see cref="DbContext"/> types that participate in tenant isolation.
/// Implemented by <see cref="MultitenantDbContext"/> and exposes the current tenant identifier
/// for query filter evaluation.
/// </summary>
public interface ITenantDbContext
{
    /// <summary>
    /// Gets the current tenant identifier for the active async execution context,
    /// or <see langword="null"/> when no tenant is active.
    /// </summary>
    TenantId? CurrentTenantId { get; }
}
