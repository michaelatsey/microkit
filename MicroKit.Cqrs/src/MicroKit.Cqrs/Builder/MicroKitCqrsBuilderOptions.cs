using System.Reflection;

namespace MicroKit.Cqrs.Builder;

/// <summary>Options for configuring MicroKit CQRS service registration.</summary>
public class MicroKitCqrsBuilderOptions
{
    /// <summary>Gets the assemblies scanned for CQRS handlers, validators, and behaviors.</summary>
    public HashSet<Assembly> Assemblies { get; } = [];
    /// <summary>Gets or sets whether query result caching is enabled.</summary>
    public bool EnableCaching { get; set; } = true;
    /// <summary>Gets or sets the default cache duration for query results.</summary>
    public TimeSpan DefaultCacheDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Adds the specified assemblies to the handler scan list.</summary>
    /// <param name="assemblies">One or more assemblies to include.</param>
    /// <returns>This options instance for fluent chaining.</returns>
    public MicroKitCqrsBuilderOptions AddAssemblies(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            Assemblies.Add(assembly);
        }
        return this;
    }
}
