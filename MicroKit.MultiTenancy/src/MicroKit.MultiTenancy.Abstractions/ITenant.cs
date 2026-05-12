using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.Abstractions;

/// <summary>Represents a resolved tenant with its identifier, display name, connection string, and extensible metadata.</summary>
public interface ITenant
{
    /// <summary>Gets the unique identifier of the tenant.</summary>
    string Id { get; }

    /// <summary>Gets the display name of the tenant, or <see langword="null"/> if not set.</summary>
    string? Name { get; }

    /// <summary>Gets the tenant-specific database connection string, or <see langword="null"/> for shared databases.</summary>
    string? ConnectionString { get; }

    /// <summary>Gets the extensible metadata dictionary for tenant-specific configuration.</summary>
    IDictionary<string, object> Items { get; }
}
