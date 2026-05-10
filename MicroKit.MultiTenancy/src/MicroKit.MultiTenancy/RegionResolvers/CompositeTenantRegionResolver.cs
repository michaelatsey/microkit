using MicroKit.MultiTenancy.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.RegionResolvers;

public class CompositeTenantRegionResolver : ITenantRegionResolver
{
    private readonly IEnumerable<ITenantRegionResolver> _resolvers;

    public CompositeTenantRegionResolver(
        IEnumerable<ITenantRegionResolver> resolvers)
    {
        _resolvers = resolvers;
    }

    public async ValueTask<string> ResolveAsync(
        string tenantIdentifier,
        CancellationToken cancellationToken = default)
    {
        foreach (var resolver in _resolvers)
        {
            var region = await resolver.ResolveAsync(
                tenantIdentifier,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(region))
                return region;
        }

        return "EU";
    }
}
