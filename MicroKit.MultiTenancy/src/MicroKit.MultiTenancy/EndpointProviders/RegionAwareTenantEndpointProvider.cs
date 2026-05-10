using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Stores;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace MicroKit.MultiTenancy.EndpointProviders;

public class RegionAwareTenantEndpointProvider : ITenantEndpointProvider
{
    private readonly RemoteTenantOptions _options;
    private readonly ITenantRegionResolver _regionResolver;

    public RegionAwareTenantEndpointProvider(
        IOptions<RemoteTenantOptions> options,
        ITenantRegionResolver regionResolver)
    {
        _options = options.Value;
        _regionResolver = regionResolver;
    }
    public async ValueTask<Uri> BuildEndpointAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var region = await _regionResolver.ResolveAsync(identifier, cancellationToken);

        var baseAddress = region switch
        {
            "EU" => new Uri("https://identity-eu.internal"),
            "US" => new Uri("https://identity-us.internal"),
            _ => _options.BaseAddress
        };

        var path = string.Format(_options.RoutePattern, identifier);

        return new Uri(baseAddress, path);
    }
}
