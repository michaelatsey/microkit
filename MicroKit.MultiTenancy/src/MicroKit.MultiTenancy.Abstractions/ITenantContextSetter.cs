using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.Abstractions;

public interface ITenantContextSetter
{
    void SetTenant(ITenant tenant);
}
