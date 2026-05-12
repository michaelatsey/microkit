using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Stores;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.EndpointProviders;

/// <summary>Builds tenant service endpoints using a configurable base address and route pattern.</summary>
public class DefaultTenantEndpointProvider : ITenantEndpointProvider
{
    private readonly RemoteTenantOptions _options;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="options">Remote tenant store options containing the base address and route pattern.</param>
    public DefaultTenantEndpointProvider(IOptions<RemoteTenantOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc/>
    public ValueTask<Uri> BuildEndpointAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var relativePath = string.Format(_options.RoutePattern, identifier);

        return ValueTask.FromResult( new Uri(_options.BaseAddress, relativePath));
    }
}

