using MicroKit.Abstractions.Serialization;
using MicroKit.MultiTenancy.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;
using System.Net.Http.Json;

namespace MicroKit.MultiTenancy.Stores;

/// <summary>Tenant store that fetches tenant data from a remote HTTP service, with a configurable cache layer.</summary>
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

    /// <summary>Initializes a new instance.</summary>
    /// <param name="clientFactory">HTTP client factory used to call the remote tenant service.</param>
    /// <param name="cache">Cache for storing resolved tenant data.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="options">Remote store configuration.</param>
    /// <param name="endpointProvider">Builds the HTTP endpoint URI for a given tenant identifier.</param>
    /// <param name="serialiser">Serializer for cache payloads.</param>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

/// <summary>Configuration options for the remote tenant HTTP store.</summary>
public class RemoteTenantOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "MicroKit:MultiTenancy:Store:RemoteTenants";
    /// <summary>Gets or sets the base address of the remote tenant service.</summary>
    public Uri BaseAddress { get; set; } = default!;
    /// <summary>Gets or sets the route pattern for individual tenant lookups (e.g. <c>api/tenants/{0}</c>).</summary>
    public string RoutePattern { get; set; } = "api/tenants/{0}";
    /// <summary>Gets or sets the route pattern for listing all tenants. Set to <see langword="null"/> to disable registry functionality.</summary>
    public string? ListRoutePattern { get; set; } = "api/tenants";
    /// <summary>Gets or sets the HTTP timeout for remote calls.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Gets or sets how long tenant data is cached after retrieval.</summary>
    public TimeSpan CacheExpirationMinutes { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Gets or sets the maximum number of tenants returned by the registry listing call.</summary>
    public int MaxTenantsToFetch { get; set; } = 1000;
}
