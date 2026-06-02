namespace MicroKit.Multitenancy;

using System.Collections.Concurrent;

/// <summary>
/// Thread-safe in-memory <see cref="ITenantStore"/> backed by a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Safe for Singleton registration.
/// </summary>
public sealed class InMemoryTenantStore : ITenantStore
{
    private readonly ConcurrentDictionary<TenantId, ITenantInfo> _tenants;

    /// <summary>Initializes an empty store.</summary>
    public InMemoryTenantStore()
        => _tenants = new ConcurrentDictionary<TenantId, ITenantInfo>();

    /// <summary>Initializes the store with a pre-seeded set of tenants.</summary>
    /// <param name="tenants">Initial tenant collection.</param>
    public InMemoryTenantStore(IEnumerable<ITenantInfo> tenants)
        => _tenants = new ConcurrentDictionary<TenantId, ITenantInfo>(
               tenants.Select(t => KeyValuePair.Create(t.Id, t)));

    /// <summary>
    /// Adds or replaces a tenant. Thread-safe.
    /// If a tenant with the same <see cref="TenantId"/> already exists, it is overwritten.
    /// </summary>
    /// <param name="tenant">The tenant to register.</param>
    public void AddTenant(ITenantInfo tenant) => _tenants[tenant.Id] = tenant;

    /// <inheritdoc/>
    public ValueTask<Result<ITenantInfo>> FindAsync(TenantId tenantId, CancellationToken ct = default)
        => _tenants.TryGetValue(tenantId, out var tenant)
            ? ValueTask.FromResult(Success(tenant))
            : ValueTask.FromResult(Failure<ITenantInfo>(MultitenancyErrors.TenantNotFound));

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<ITenantInfo>> ListAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ITenantInfo> list = [.. _tenants.Values];
        return ValueTask.FromResult(list);
    }
}
