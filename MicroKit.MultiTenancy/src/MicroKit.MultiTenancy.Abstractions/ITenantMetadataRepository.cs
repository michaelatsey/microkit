using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.Abstractions;

/// <summary>Persistence contract for reading tenant metadata records.</summary>
public interface ITenantMetadataRepository
{
    /// <summary>Retrieves the metadata for the tenant with the given identifier.</summary>
    /// <param name="tenantIdentifier">The tenant identifier to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tenant metadata, or <see langword="null"/> if not found.</returns>
    Task<TenantMetadata?> GetByIdAsync(string tenantIdentifier, CancellationToken cancellationToken);
}
