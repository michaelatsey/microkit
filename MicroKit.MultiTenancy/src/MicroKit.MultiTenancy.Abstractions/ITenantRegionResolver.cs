using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.Abstractions;

public interface ITenantRegionResolver
{
    ValueTask<string> ResolveAsync(
        string tenantIdentifier,
        CancellationToken cancellationToken = default);
}
