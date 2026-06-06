namespace MicroKit.Multitenancy.EntityFrameworkCore;

/// <summary>
/// <see cref="ITenantStore"/> implementation backed by a <see cref="TenantStoreDbContext"/>.
/// Provides durable, database-backed tenant lookup.
/// </summary>
/// <remarks>
/// Register via <see cref="EfIsolationBuilder.UseEfStore"/>. The underlying
/// <see cref="TenantStoreDbContext"/> must be configured separately via
/// <c>DbContextOptionsBuilder</c> options passed to <see cref="EfIsolationBuilder.UseEfStore"/>.
/// Both this service and the DbContext are Scoped.
/// </remarks>
public sealed class EfTenantStore(TenantStoreDbContext context) : ITenantStore
{
    /// <inheritdoc/>
    public async ValueTask<Result<ITenantInfo>> FindAsync(TenantId tenantId, CancellationToken ct = default)
    {
        var record = await context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId.Value, ct)
            .ConfigureAwait(false);

        return record is null
            ? Failure<ITenantInfo>(MultitenancyErrors.TenantNotFound)
            : Success<ITenantInfo>(record.ToTenantInfo());
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<ITenantInfo>> ListAllAsync(CancellationToken ct = default)
    {
        var records = await context.Tenants
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return records.ConvertAll(r => r.ToTenantInfo());
    }
}
