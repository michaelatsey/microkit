using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.Abstractions;

public class TenantMetadata
{
    public string Id { get; set; } = default!;
    public string Region { get; set; } = default!;
}
