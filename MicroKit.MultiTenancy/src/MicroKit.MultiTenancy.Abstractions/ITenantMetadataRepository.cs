using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.Abstractions;

public interface ITenantMetadataRepository
{
    Task<TenantMetadata?> GetByIdAsync(string tenantIdentifier, CancellationToken cancellationToken);
}
