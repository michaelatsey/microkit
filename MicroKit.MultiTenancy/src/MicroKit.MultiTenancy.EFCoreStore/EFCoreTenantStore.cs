using MicroKit.Abstractions.Serialization;
using MicroKit.MultiTenancy.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;

namespace MicroKit.MultiTenancy.EFCoreStore;

public class EFCoreTenantStore<TContext> : ITenantStore, ITenantRegistry
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITenantCache _cache;
    private readonly IMicroKitSerializer _serializer;
    private readonly DatabaseTenantOptions _options;
    private readonly ILogger<EFCoreTenantStore<TContext>> _logger;

    private const string CachePrefix = "tenant:db:";

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

public class DatabaseTenantOptions
{
    // Durée de vie du cache pour éviter de requêter la DB à chaque appel
    public TimeSpan CacheExpirationMinutes { get; set; } = TimeSpan.FromMinutes(60);

    // Possibilité de désactiver le listing si la table est trop énorme (cas rares)
    public bool EnableRegistry { get; set; } = true;
}