using System.Collections.ObjectModel;

namespace MicroKit.MultiTenancy.Abstractions;

/// <summary>Provides a list of all registered tenant identifiers in the system.</summary>
public interface ITenantRegistry
{
    /// <summary>Returns the identifiers of all registered tenants.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only collection of tenant identifiers.</returns>
    Task<ReadOnlyCollection<string>> GetAllTenantsAsync(CancellationToken cancellationToken = default);
}
