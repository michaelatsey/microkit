using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.Abstractions;

/// <summary>Resolves a tenant by identifier from the underlying store (database, cache, or configuration).</summary>
public interface ITenantStore
{
    /// <summary>Returns the tenant for the given identifier, or <see langword="null"/> if not found.</summary>
    /// <param name="identifier">The tenant identifier to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ITenant?> GetTenantAsync(string identifier, CancellationToken cancellationToken = default);
}
