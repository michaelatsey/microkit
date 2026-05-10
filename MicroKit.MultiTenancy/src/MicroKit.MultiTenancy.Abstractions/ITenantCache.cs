using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.Abstractions;

public interface ITenantCache
{
    Task<string?> GetAsync( string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken = default);
}
