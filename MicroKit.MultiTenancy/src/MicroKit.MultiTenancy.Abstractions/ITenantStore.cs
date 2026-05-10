using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.Abstractions;

public interface ITenantStore
{
    Task<ITenant?> GetTenantAsync(string identifier, CancellationToken cancellationToken = default);
}
