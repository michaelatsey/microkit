using MicroKit.MultiTenancy.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy;

/// <summary>Default mutable implementation of <see cref="ITenant"/>.</summary>
public class Tenant : ITenant
{
    /// <inheritdoc/>
    public string Id { get; set; }

    /// <inheritdoc/>
    public string? Name { get; set; }

    /// <inheritdoc/>
    public string? ConnectionString { get; set; }

    /// <inheritdoc/>
    public IDictionary<string, object> Items { get; set; }

    /// <summary>Initializes a new tenant instance.</summary>
    /// <param name="id">The unique tenant identifier.</param>
    /// <param name="name">Optional display name.</param>
    /// <param name="connectionString">Optional tenant-specific database connection string.</param>
    /// <param name="items">Optional metadata dictionary.</param>
    public Tenant(string id, string? name = null, string? connectionString = null, IDictionary<string, object>? items = null)
    {
        Id = id;
        Name = name;
        ConnectionString = connectionString;
        Items = items ?? new Dictionary<string, object>();
    }

}
