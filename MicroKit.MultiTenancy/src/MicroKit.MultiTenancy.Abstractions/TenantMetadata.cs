using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.Abstractions;

/// <summary>Stores the persisted metadata for a tenant, including its deployment region.</summary>
public class TenantMetadata
{
    /// <summary>Gets or sets the unique tenant identifier.</summary>
    public string Id { get; set; } = default!;

    /// <summary>Gets or sets the deployment region for this tenant (e.g. <c>eu-west-1</c>).</summary>
    public string Region { get; set; } = default!;
}
