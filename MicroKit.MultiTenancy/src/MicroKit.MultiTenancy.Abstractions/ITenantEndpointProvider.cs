using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.Abstractions;

public interface ITenantEndpointProvider
{
    ValueTask<Uri> BuildEndpointAsync(string identifier, CancellationToken cancellationToken = default);
}
