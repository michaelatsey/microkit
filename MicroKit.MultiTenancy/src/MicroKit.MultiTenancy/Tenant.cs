using MicroKit.MultiTenancy.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy;

public class Tenant : ITenant
{
    public string Id { get; set; }

    public string? Name { get; set; }

    public string? ConnectionString { get; set; }

    public IDictionary<string, object> Items { get; set; }

    public Tenant(string id, string? name = null, string? connectionString = null, IDictionary<string, object>? items = null)
    {
        Id = id;
        Name = name;
        ConnectionString = connectionString;
        Items = items ?? new Dictionary<string, object>();
    }

}
