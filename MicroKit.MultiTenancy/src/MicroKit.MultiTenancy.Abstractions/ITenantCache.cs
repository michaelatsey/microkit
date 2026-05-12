using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.Abstractions;

/// <summary>Provides a two-level cache (L1 memory + L2 distributed) scoped to tenant data.</summary>
public interface ITenantCache
{
    /// <summary>Gets a cached value by key, or <see langword="null"/> if not found.</summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Stores a value with the given time-to-live.</summary>
    Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>Removes a cached value, invalidating both L1 and L2 caches if applicable.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
