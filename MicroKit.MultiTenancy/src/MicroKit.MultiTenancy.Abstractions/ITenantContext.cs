using MicroKit.Abstractions.Contexts;

namespace MicroKit.MultiTenancy.Abstractions;

/// <summary>
/// Holds the resolved tenant for the current request scope.
/// Implements <see cref="ITenantIdAccessor"/> so the tenant ID is available
/// to any service that only needs the identifier.
/// </summary>
public interface ITenantContext : ITenantIdAccessor
{
    /// <summary>Gets the fully resolved tenant, or <see langword="null"/> if not yet resolved.</summary>
    ITenant? Tenant { get; }

    /// <summary>Gets a value indicating whether a tenant has been resolved for the current scope.</summary>
    bool IsResolved { get; }

    /// <summary>Throws <see cref="InvalidOperationException"/> if no tenant has been resolved.</summary>
    void EnsureResolved();

    /// <inheritdoc />
    string? ITenantIdAccessor.TenantId => Tenant?.Id;
}
