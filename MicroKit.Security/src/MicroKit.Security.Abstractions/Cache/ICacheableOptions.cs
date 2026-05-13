using MicroKit.Security.Abstractions.Options;

namespace MicroKit.Security.Abstractions.Cache;

/// <summary>Marker interface for options classes that expose a <see cref="CacheOptions"/> property for two-level caching configuration.</summary>
public interface ICacheableOptions
{
    /// <summary>Gets the cache configuration for this options instance.</summary>
    CacheOptions Cache { get; }
}
