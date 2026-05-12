using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.Abstractions;

/// <summary>Allows infrastructure code (middleware, interceptors) to push a resolved tenant into the current scope.</summary>
public interface ITenantContextSetter
{
    /// <summary>Sets the active tenant for the current request scope.</summary>
    /// <param name="tenant">The resolved tenant to set.</param>
    void SetTenant(ITenant tenant);
}
