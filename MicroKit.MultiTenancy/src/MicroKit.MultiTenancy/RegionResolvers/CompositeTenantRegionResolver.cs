using MicroKit.MultiTenancy.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.RegionResolvers;

/// <summary>Chains multiple <see cref="ITenantRegionResolver"/> instances and returns the first non-empty result.</summary>
public class CompositeTenantRegionResolver : ITenantRegionResolver
{
    private readonly IEnumerable<ITenantRegionResolver> _resolvers;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="resolvers">Ordered sequence of resolvers to try.</param>
    public CompositeTenantRegionResolver(
        IEnumerable<ITenantRegionResolver> resolvers)
    {
        _resolvers = resolvers;
    }

    /// <inheritdoc/>
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
