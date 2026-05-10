using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace MicroKit.Cqrs.Builder;

public class MicroKitCqrsBuilderOptions
{
    // Liste des assemblies contenant les Handlers, Validators, etc.
    public HashSet<Assembly> Assemblies { get; } = [];
    public bool EnableCaching { get; set; } = true;
    public TimeSpan DefaultCacheDuration { get; set; } = TimeSpan.FromMinutes(15);

    public MicroKitCqrsBuilderOptions AddAssemblies(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            Assemblies.Add(assembly);
        }
        return this;
    }
}
