using MicroKit.MultiTenancy.Abstractions;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.RegionResolvers;

/// <summary>Resolves tenant regions from a static configuration map.</summary>
public class ConfigurationTenantRegionResolver : ITenantRegionResolver
{
    private readonly TenantRegionOptions _options;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="options">Options containing the tenant-to-region mapping.</param>
    public ConfigurationTenantRegionResolver(IOptions<TenantRegionOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc/>
    public ValueTask<string> ResolveAsync(
        string tenantIdentifier,
        CancellationToken cancellationToken = default)
    {
        if (_options.TenantRegions.TryGetValue(
                tenantIdentifier,
                out var region))
        {
            return ValueTask.FromResult(region);
        }

        return ValueTask.FromResult(_options.DefaultRegion);
    }
}

/// <summary>Configuration options mapping tenant identifiers to their hosting regions.</summary>
public class TenantRegionOptions
{
    /// <summary>Gets or sets the per-tenant region map.</summary>
    public Dictionary<string, string> TenantRegions { get; set; } = [];
    /// <summary>Gets or sets the region used for tenants not found in <see cref="TenantRegions"/>.</summary>
    public string DefaultRegion { get; set; } = "EU";
}