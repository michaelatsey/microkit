namespace MicroKit.Multitenancy;

/// <summary>
/// Read/write accessor for the current tenant context, backed by <c>AsyncLocal</c>.
/// Must be registered as <b>Scoped</b> — never Singleton (MKT003).
/// </summary>
public interface ITenantContextAccessor : ITenantContext
{
    /// <summary>Sets the current tenant for the active async execution context.</summary>
    /// <param name="tenant">The tenant to set, or <see langword="null"/> to clear.</param>
    void SetTenant(ITenantInfo? tenant);

    /// <summary>
    /// Creates a scoped tenant context that restores the previous tenant on disposal.
    /// Required for background tasks and parallel work items.
    /// </summary>
    /// <param name="tenant">The tenant to activate for the scope duration.</param>
    /// <returns>An <see cref="IDisposable"/> that restores the previous tenant when disposed.</returns>
    IDisposable CreateScope(ITenantInfo tenant);
}
