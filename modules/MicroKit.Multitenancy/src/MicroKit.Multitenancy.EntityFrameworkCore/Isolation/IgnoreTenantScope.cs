namespace MicroKit.Multitenancy.EntityFrameworkCore;

/// <summary>
/// Creates a scope in which the global tenant query filter is suspended for the current
/// async execution context. Required for cross-tenant admin and reporting queries.
/// </summary>
/// <remarks>
/// <para>
/// Callers MUST annotate the query with a justification comment to satisfy the MKT002 analyzer:
/// <code>
/// using var _ = new IgnoreTenantScope();
/// var allOrders = await context.Orders
///     // [MTK-BYPASS] Admin report: aggregating across all tenants for billing summary
///     .IgnoreQueryFilters()
///     .ToListAsync(ct);
/// </code>
/// </para>
/// <para>
/// Nested <see cref="IgnoreTenantScope"/> instances are supported. Each restores the previous
/// bypass state on disposal, following the same restore-previous-value pattern as
/// <see cref="ITenantContextAccessor.CreateScope"/>.
/// </para>
/// </remarks>
public sealed class IgnoreTenantScope : IDisposable
{
    private static readonly AsyncLocal<bool> _active = new();

    /// <summary>Gets a value indicating whether tenant filtering is currently suspended in the active async context.</summary>
    internal static bool IsActive => _active.Value;

    private readonly bool _previous;

    /// <summary>
    /// Initializes a new scope and suspends tenant filtering for the current async execution context.
    /// </summary>
    public IgnoreTenantScope()
    {
        _previous = _active.Value;
        _active.Value = true;
    }

    /// <summary>Restores the previous tenant filtering state.</summary>
    public void Dispose() => _active.Value = _previous;
}
