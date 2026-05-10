using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Stores;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.EndpointProviders;

public class DefaultTenantEndpointProvider : ITenantEndpointProvider
{
    private readonly RemoteTenantOptions _options;

    public DefaultTenantEndpointProvider(IOptions<RemoteTenantOptions> options)
    {
        _options = options.Value;
    }

    public ValueTask<Uri> BuildEndpointAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var relativePath = string.Format(_options.RoutePattern, identifier);

        return ValueTask.FromResult( new Uri(_options.BaseAddress, relativePath));
    }
}

