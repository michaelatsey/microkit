using MicroKit.Abstractions.Serialization;
using MicroKit.MultiTenancy.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;
using System.Net.Http.Json;

namespace MicroKit.MultiTenancy.Stores;

public class RemoteTenantStore : ITenantStore, ITenantRegistry
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly IMicroKitSerializer _serialiser;
    //private readonly IMemoryCache _memoryCache;
    //private readonly IDistributedCache? _distributedCache;
    private readonly ITenantCache _cache;
    private readonly ILogger<RemoteTenantStore> _logger;
    private readonly ITenantEndpointProvider _endpointProvider;
    private readonly RemoteTenantOptions _options;

    private const string CachePrefix = "tenant:";
    private const string ListCacheKey = "tenants:all_ids";

    public RemoteTenantStore(
        IHttpClientFactory clientFactory,
        ITenantCache cache,
        ILogger<RemoteTenantStore> logger,
        IOptions<RemoteTenantOptions> options,
        ITenantEndpointProvider endpointProvider,
        IMicroKitSerializer serialiser
        )
    {
        _clientFactory = clientFactory;
        _cache = cache;
        _logger = logger;
        _options = options.Value;
        _endpointProvider = endpointProvider;
        _serialiser = serialiser;
    }

    public async Task<ITenant?> GetTenantAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CachePrefix}{identifier}";

        // 1. HIT CACHE
        var cached = await _cache.GetAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return _serialiser.Deserialize<Tenant>(cached);
        }
        
        // 2. Appel au Microservice Tenant (via HttpClient résilient)
        try
        {
            var client = _clientFactory.CreateClient("TenantService");
            var endpoint = await _endpointProvider.BuildEndpointAsync(identifier, cancellationToken);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Calling tenant endpoint {Endpoint}", endpoint);
            }
            var response = await client.GetAsync(endpoint, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;

            response.EnsureSuccessStatusCode();

            

            var tenant = await response.Content.ReadFromJsonAsync<ITenant>(cancellationToken: cancellationToken);

            if (tenant is not null)
            {
                await CacheTenantAsync(cacheKey, tenant, cancellationToken);
            }    

            return tenant;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RemoteTenantStore: Error fetching tenant {Id}", identifier);
            return null; // Le Middleware renverra un 401/403
        }

        
    }

    public async Task<ReadOnlyCollection<string>> GetAllTenantsAsync(CancellationToken cancellationToken = default)
    {
        // Si aucun pattern n'est configuré, ce store ne supporte pas le listing
        if (string.IsNullOrWhiteSpace(_options.ListRoutePattern))
        {
            _logger.LogWarning("RemoteTenantStore: ListRoutePattern is not configured. Registry functionality is disabled.");
            return [];
        }

        // 1. On tente de récupérer la liste depuis le cache (pour soulager le service central)
        var cachedList = await _cache.GetAsync(ListCacheKey, cancellationToken);
        if (cachedList is not null)
        {
            return _serialiser.Deserialize<ReadOnlyCollection<string>>(cachedList) ?? [];
        }

        try
        {
            var client = _clientFactory.CreateClient("TenantService");

            // PRO : On utilise le même principe de pattern pour la liste
            // On peut passer "" car le pattern de liste n'utilise pas d'index {0}
            var endpoint = _options.ListRoutePattern;

            var ids = await client.GetFromJsonAsync<ReadOnlyCollection<string>>(endpoint, cancellationToken);

            if (ids != null)
            {
                // On cache la liste des IDs pour une durée plus courte que les tenants individuels
                await _cache.SetAsync(
                    ListCacheKey,
                    _serialiser.Serialize(ids),
                    TimeSpan.FromMinutes(10),
                    cancellationToken);

                return ids;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RemoteTenantStore: Failed to fetch tenant list from {Endpoint}", _options.ListRoutePattern);
        }

        return [];
    }

    private async Task CacheTenantAsync(string key, ITenant tenant, CancellationToken cancellationToken)
    {
        var json = _serialiser.Serialize(tenant);
        await _cache.SetAsync(
            key, 
            json, 
            _options.CacheExpirationMinutes, 
            cancellationToken);
    }
}

public class RemoteTenantOptions
{
    public const string SectionName = "MicroKit:MultiTenancy:Store:RemoteTenants";
    public Uri BaseAddress { get; set; } = default!;
    // Exemple : "api/tenants/{0}" ou "internal/clients/get?id={0}"
    public string RoutePattern { get; set; } = "api/tenants/{0}";
    // Pattern pour lister TOUS les tenants (utilisé par ITenantRegistry)
    // Si null, le registry sera considéré comme désactivé pour ce store.
    public string? ListRoutePattern { get; set; } = "api/tenants";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan CacheExpirationMinutes { get; set; } = TimeSpan.FromMinutes(30);

    public int MaxTenantsToFetch { get; set; } = 1000;
}
