using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.MultiTenancy.Abstractions;

public interface ITenant
{
    string Id { get; }
    string? Name { get; }
    string? ConnectionString { get; }
    IDictionary<string, object> Items { get; } // Pour des métadonnées extensibles
}
