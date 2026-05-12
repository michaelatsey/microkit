using MicroKit.Abstractions.Serialization;
using MicroKit.MultiTenancy.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;

namespace MicroKit.MultiTenancy.EFCoreStore;

/// <summary>EF Core-backed implementation of <see cref="ITenantStore"/> and <see cref="ITenantRegistry"/> with integrated caching.</summary>
public class EFCoreTenantStore<TContext> : ITenantStore, ITenantRegistry
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITenantCache _cache;
    private readonly IMicroKitSerializer _serializer;
    private readonly DatabaseTenantOptions _options;
    private readonly ILogger<EFCoreTenantStore<TContext>> _logger;

    private const string CachePrefix = "tenant:db:";

    /// <summary>Initializes a new instance.</summary>
    /// <param name="scopeFactory">Factory for creating dependency injection scopes.</param>
    /// <param name="cache">Tenant cache for reducing database lookups.</param>
    /// <param name="serializer">Serializer used to cache tenant data.</param>
    /// <param name="options">Database tenant store options.</param>
    /// <param name="logger">Logger instance.</param>
    public EFCoreTenantStore(
        IServiceScopeFactory scopeFactory,
        ITenantCache cache,
        IMicroKitSerializer serializer,
        IOptions<DatabaseTenantOptions> options,
        ILogger<EFCoreTenantStore<TContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _serializer = serializer;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ITenant?> GetTenantAsync(string tenantId, CancellationToken ct = default)
    {
        var cacheKey = $"{CachePrefix}{tenantId}";

        // 1. Recherche en Cache
        var cached = await _cache.GetAsync(cacheKey, ct);
        if (cached != null)
        {
            return _serializer.Deserialize<Tenant>(cached);
        }

        // 2. Recherche en Base de données
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        // On suppose que l'entité implémente ITenant ou est mappable
        // L'Expert utilise souvent Set<Tenant>() ou une interface dédiée
        var tenantEntity = await context.Set<Tenant>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        if (tenantEntity != null)
        {
            await _cache.SetAsync(cacheKey, _serializer.Serialize(tenantEntity), _options.CacheExpirationMinutes, ct);
        }

        return tenantEntity;
    }

    /// <inheritdoc/>
    public async Task<ReadOnlyCollection<string>> GetAllTenantsAsync(CancellationToken ct = default)
    {
        if (!_options.EnableRegistry) return [];

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        // 3. Extraction optimisée (Projection uniquement sur l'ID)
        var tenantIds = await context.Set<Tenant>() // Assure-toi que 'Tenant' est l'entité mappée
            .AsNoTracking()
            .Select(t => t.Id)
            .ToListAsync(ct);

        // 4. Conversion en lecture seule pour protéger l'intégrité des données
        return tenantIds.AsReadOnly();
    }
}

/// <summary>Options for the EF Core-backed tenant store.</summary>
public class DatabaseTenantOptions
{
    /// <summary>Gets or sets the cache time-to-live for tenant lookups.</summary>
    public TimeSpan CacheExpirationMinutes { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>Gets or sets whether the tenant registry listing is enabled.</summary>
    public bool EnableRegistry { get; set; } = true;
}