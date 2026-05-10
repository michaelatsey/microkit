using MicroKit.MultiTenancy.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace MicroKit.MultiTenancy.Stores;

public class PassThroughTenantStore : ITenantStore, ITenantRegistry, IDisposable
{
    private readonly PassThroughTenantOptions _options;
    private readonly ILogger<PassThroughTenantStore> _logger;

    // PRO : Cache mémoire interne pour éviter de recréer l'objet Tenant à chaque requête
    // On utilise un ConcurrentDictionary car le Store est Scoped ou Singleton
    private static readonly ConcurrentDictionary<string, ITenant> _tenantCache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);
    private Timer? _cacheCleanupTimer;

    public PassThroughTenantStore(
        IOptions<PassThroughTenantOptions> options,
        ILogger<PassThroughTenantStore> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Initialisation et validation
        ValidateAndCleanOptions();

        // Cache cleanup timer
        _cacheCleanupTimer = new Timer(CleanupCache, null, _cacheDuration, _cacheDuration);
    }

    public Task<ITenant?> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Task.FromResult<ITenant?>(null);
        }

        // PRO : Pattern de cache "Flyweight" pour économiser la mémoire
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Task.FromResult<ITenant?>(null);
        }

        // Normalize tenant ID
        tenantId = tenantId.Trim().ToLowerInvariant();

        var tenant = _tenantCache.GetOrAdd(tenantId, CreateVirtualTenant);

        return Task.FromResult<ITenant?>(tenant);
    }



    /// <summary>
    /// Implémentation de ITenantRegistry pour le PassThrough.
    /// Comme il n'y a pas de DB, on se base sur la liste fournie en configuration.
    /// </summary>
    public Task<ReadOnlyCollection<string>> GetAllTenantsAsync(CancellationToken cancellationToken = default)
    {
        ReadOnlyCollection<string> tenants = _options.StaticTenants.AsReadOnly();

        if (tenants.Count == 0)
        {
            _logger.LogWarning("PassThroughStore: No static tenants configured. Workers might not process any data.");
        }

        return Task.FromResult(tenants);
    }

    private ITenant CreateVirtualTenant(string tenantId)
    {
        _logger.LogDebug("Creating virtual tenant with ID: {TenantId}", tenantId);

        var metadata = _options.DefaultMetadata
            .ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);

        // Ajouter des métadonnées utiles
        metadata["created_at"] = DateTime.UtcNow;
        metadata["source"] = "PassThroughTenantStore";
        metadata["is_virtual"] = true;

        return new Tenant(
            tenantId,
            $"{_options.DefaultTenantName} [{tenantId}]",
            null, // No specific connection string for virtual tenants
            metadata);
    }

    // Méthode utilitaire pour nettoyer le cache périodiquement
    private void CleanupCache(object? state)
    {
        try
        {
            _cacheLock.Wait();

            var expiredTenants = _tenantCache
                .Where(kvp => kvp.Value.Items.TryGetValue("created_at", out var createdAt) &&
                              createdAt is DateTime dt &&
                              DateTime.UtcNow - dt > _cacheDuration)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var tenantId in expiredTenants)
            {
                _tenantCache.TryRemove(tenantId, out _);
                _logger.LogDebug("Removed expired tenant from cache: {TenantId}", tenantId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cache cleanup");
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public void Dispose()
    {
        _cacheCleanupTimer?.Dispose();
        _cacheLock?.Dispose();
    }

    private void ValidateAndCleanOptions()
    {
        // Déduplication et validation des tenants statiques
        _options.StaticTenants = _options.StaticTenants
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_options.StaticTenants.Count == 0)
        {
            _logger.LogWarning("No static tenants configured. Workers might not process any data.");
        }

        // Validation des métadonnées
        _options.DefaultMetadata ??= [];
    }
}

public class PassThroughTenantOptions
{
    /// <summary>
    /// Liste des identifiants de tenants connus. 
    /// INDISPENSABLE pour que les Cleanup Workers sachent quels tenants traiter.
    /// </summary>
    public List<string> StaticTenants { get; set; } = [];

    /// <summary>
    /// Nom par défaut attribué aux tenants créés à la volée.
    /// </summary>
    public string DefaultTenantName { get; set; } = "Virtual Tenant";

    /// <summary>
    /// Metadata par défaut injectées dans chaque tenant virtuel.
    /// </summary>
    public Dictionary<string, string> DefaultMetadata { get; set; } = new();
}