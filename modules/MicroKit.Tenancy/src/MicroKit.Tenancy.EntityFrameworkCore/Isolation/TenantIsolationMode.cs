namespace MicroKit.Tenancy.EntityFrameworkCore;

/// <summary>Controls how tenant data is physically isolated in the database.</summary>
public enum TenantIsolationMode
{
    /// <summary>
    /// All tenants share tables. Rows are discriminated by a <c>TenantId</c> column.
    /// Global query filters and the <see cref="TenantStampInterceptor"/> enforce row-level isolation.
    /// Phase 1 — fully implemented.
    /// </summary>
    Shared,

    /// <summary>
    /// Each tenant uses a dedicated database schema (e.g., <c>tenant_{id}.Orders</c>).
    /// Phase 2 — not yet implemented.
    /// </summary>
    Schema,

    /// <summary>
    /// Each tenant uses a dedicated database with its own connection string from
    /// <see cref="ITenantInfo.ConnectionString"/>.
    /// Phase 2 — not yet implemented.
    /// </summary>
    Database,
}
