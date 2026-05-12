using System.Text.Json;

namespace MicroKit.Caching.Distributed;

/// <summary>
/// Configuration options for <see cref="DistributedCacheService"/>.
/// </summary>
public sealed class DistributedCacheOptions
{
    /// <summary>
    /// JSON serializer options used when serialising and deserialising cached values.
    /// Defaults to case-insensitive property matching.
    /// </summary>
    public JsonSerializerOptions SerializerOptions { get; set; } = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
