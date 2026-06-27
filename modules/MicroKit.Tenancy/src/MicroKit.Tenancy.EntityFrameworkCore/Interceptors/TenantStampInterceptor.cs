namespace MicroKit.Tenancy.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// EF Core <see cref="SaveChangesInterceptor"/> that stamps <see cref="ITenantEntity.TenantId"/> on
/// every <c>Added</c> entity before the changes are persisted.
/// </summary>
/// <remarks>
/// <para>
/// Must be registered as <b>Scoped</b> and added to the DbContext via
/// <see cref="DbContextOptionsBuilderExtensions.AddTenantStamping"/> or
/// <c>DbContextOptionsBuilder.AddInterceptors</c>.
/// </para>
/// <para>
/// The interceptor always stamps the current tenant, overwriting any <see cref="ITenantEntity.TenantId"/>
/// value previously set by the caller. This guarantees that the current tenant context wins and prevents
/// cross-tenant writes from stale or manually-set identifiers.
/// </para>
/// <para>
/// Throws <see cref="InvalidOperationException"/> when <see cref="ITenantContext.CurrentTenant"/>
/// is <see langword="null"/> for any <c>Added</c> <see cref="ITenantEntity"/> entry — writes without
/// an active tenant context are disallowed.
/// </para>
/// </remarks>
public sealed class TenantStampInterceptor(ITenantContextAccessor accessor) : SaveChangesInterceptor
{
    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        StampTenantId(eventData.Context!);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        StampTenantId(eventData.Context!);
        return base.SavingChanges(eventData, result);
    }

    private void StampTenantId(DbContext context)
    {
        var addedEntities = context.ChangeTracker
            .Entries<ITenantEntity>()
            .Where(e => e.State == EntityState.Added)
            .ToList();

        if (addedEntities.Count == 0)
            return;

        // Resolve tenant once for all Added entries in this SaveChanges call.
        var tenantId = accessor.CurrentTenant?.Id
            ?? throw new InvalidOperationException(
                "Cannot save changes without an active tenant context. " +
                "Ensure ITenantContextAccessor has a tenant set before calling SaveChangesAsync.");

        foreach (var entry in addedEntities)
        {
            // Always stamps the current tenant — overrides any manually-set TenantId.
            // This guarantees the current tenant context wins and prevents cross-tenant writes.
            entry.Property(nameof(ITenantEntity.TenantId)).CurrentValue = tenantId;
        }
    }
}
