using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.Abstractions;

/// <summary>Resolves the deployment region for a given tenant, enabling geo-routing.</summary>
public interface ITenantRegionResolver
{
    /// <summary>Returns the region identifier for the given tenant.</summary>
    /// <param name="tenantIdentifier">The tenant identifier whose region to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The region name (e.g. <c>eu-west-1</c>).</returns>
    ValueTask<string> ResolveAsync(
        string tenantIdentifier,
        CancellationToken cancellationToken = default);
}
