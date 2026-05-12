using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.Abstractions;

/// <summary>Builds tenant-specific endpoint URIs (e.g. per-tenant API gateways or storage accounts).</summary>
public interface ITenantEndpointProvider
{
    /// <summary>Resolves the endpoint URI for the given tenant identifier.</summary>
    /// <param name="identifier">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The fully-qualified <see cref="Uri"/> for the tenant's endpoint.</returns>
    ValueTask<Uri> BuildEndpointAsync(string identifier, CancellationToken cancellationToken = default);
}
