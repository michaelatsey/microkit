using MicroKit.MultiTenancy.Abstractions;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.RegionResolvers;

public class ConfigurationTenantRegionResolver : ITenantRegionResolver
{
    private readonly TenantRegionOptions _options;

    public ConfigurationTenantRegionResolver(IOptions<TenantRegionOptions> options)
    {
        _options = options.Value;
    }

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

public class TenantRegionOptions
{
    public Dictionary<string, string> TenantRegions { get; set; } = [];
    public string DefaultRegion { get; set; } = "EU";
}