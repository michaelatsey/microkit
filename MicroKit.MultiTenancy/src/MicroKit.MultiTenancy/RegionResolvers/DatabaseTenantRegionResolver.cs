using MicroKit.MultiTenancy.Abstractions;

namespace MicroKit.MultiTenancy.RegionResolvers;

/// <summary>Resolves tenant regions from a metadata repository, with an in-process cache to reduce database load.</summary>
public class DatabaseTenantRegionResolver : ITenantRegionResolver
{
    private readonly ITenantMetadataRepository _repository;
    private readonly ITenantCache _cache;

    private const string CachePrefix = "tenant-region:";

    /// <summary>Initializes a new instance.</summary>
    /// <param name="repository">Repository for loading tenant metadata.</param>
    /// <param name="cache">Cache for storing resolved regions.</param>
    public DatabaseTenantRegionResolver(
        ITenantMetadataRepository repository,
        ITenantCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    /// <inheritdoc/>
    public async ValueTask<string> ResolveAsync(string tenantIdentifier,CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CachePrefix}{tenantIdentifier}";
        
        var cached = await _cache.GetAsync(cacheKey, cancellationToken);
        if (cached is not null)
            return cached;

        // DB lookup
        var metadata = await _repository.GetByIdAsync(tenantIdentifier, cancellationToken);

        var region = metadata?.Region ?? "EU";

        // Store in memory cache
        await _cache.SetAsync(cacheKey, region, TimeSpan.FromMinutes(5), cancellationToken);

        return region;
    }
}
