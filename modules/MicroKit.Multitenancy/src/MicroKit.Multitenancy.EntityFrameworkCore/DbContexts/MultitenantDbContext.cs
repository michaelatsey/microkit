namespace MicroKit.Multitenancy.EntityFrameworkCore;

using System.Reflection;

/// <summary>
/// Abstract base <see cref="DbContext"/> that automatically applies global query filters to every
/// <see cref="ITenantEntity"/> in the model and exposes <see cref="CurrentTenantId"/> for filter
/// evaluation at query time.
/// </summary>
/// <remarks>
/// <para>
/// Subclass this context and call <c>base.OnModelCreating(modelBuilder)</c> to activate tenant isolation.
/// The interceptor that stamps <see cref="ITenantEntity.TenantId"/> on <c>Added</c> entries is registered
/// separately via <c>AddInterceptors</c> using
/// <see cref="DbContextOptionsBuilderExtensions.AddTenantStamping"/>.
/// </para>
/// <para>
/// To bypass the global filter for admin/reporting queries, wrap the query in an
/// <see cref="IgnoreTenantScope"/> and annotate with <c>// [MTK-BYPASS] reason</c> (MKT002).
/// </para>
/// </remarks>
public abstract class MultitenantDbContext : DbContext, ITenantDbContext
{
    // Cached once per AppDomain — safe for concurrent use, reflection on a known method.
    private static readonly MethodInfo _applyFilterMethod =
        typeof(MultitenantDbContext).GetMethod(
            nameof(ApplyQueryFilterForEntity),
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly ITenantContextAccessor _tenantAccessor;

    /// <summary>
    /// Initializes a new instance of <see cref="MultitenantDbContext"/>.
    /// </summary>
    /// <param name="options">The EF Core options for this context.</param>
    /// <param name="tenantAccessor">
    /// The accessor used to read the current tenant from the async execution context.
    /// Must be Scoped — never Singleton (MKT003).
    /// </param>
    protected MultitenantDbContext(DbContextOptions options, ITenantContextAccessor tenantAccessor)
        : base(options)
    {
        _tenantAccessor = tenantAccessor;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Evaluated per-query by EF Core via the instance property reference captured in the
    /// global query filter expression. Returns <see langword="null"/> when no tenant is active,
    /// causing the filter to match zero rows (safe default).
    /// </remarks>
    public virtual TenantId? CurrentTenantId => _tenantAccessor.CurrentTenant?.Id;

    /// <summary>
    /// Gets a value indicating whether the tenant query filter is currently active.
    /// Returns <see langword="false"/> when <see cref="IgnoreTenantScope.IsActive"/> is <see langword="true"/>.
    /// </summary>
    protected virtual bool TenantFilterEnabled => !IgnoreTenantScope.IsActive;

    /// <inheritdoc/>
    /// <remarks>
    /// Applies global query filters to all <see cref="ITenantEntity"/> root types.
    /// Only root types are processed — derived types in TPH/TPT hierarchies are skipped
    /// to prevent the filter from being evaluated twice, which would produce incorrect SQL.
    /// Subclasses must call <c>base.OnModelCreating(modelBuilder)</c>.
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // t.BaseType == null — apply filter to inheritance root types only.
        // Derived types in TPH/TPT hierarchies inherit the filter; re-applying it would
        // cause the filter to be evaluated twice, producing incorrect SQL.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
            .Where(t => typeof(ITenantEntity).IsAssignableFrom(t.ClrType) && t.BaseType == null))
        {
            _applyFilterMethod
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(this, [modelBuilder]);
        }
    }

    /// <summary>
    /// Configures the value converter and global query filter for a single <see cref="ITenantEntity"/> type.
    /// Called via reflection from <see cref="OnModelCreating"/> once per root entity type.
    /// </summary>
    /// <remarks>
    /// Filter shape: <c>e => !TenantFilterEnabled || e.TenantId == CurrentTenantId</c>
    /// <list type="bullet">
    /// <item>When bypass is active (<see cref="IgnoreTenantScope.IsActive"/> = true): all rows visible.</item>
    /// <item>When no tenant (<see cref="CurrentTenantId"/> = null): zero rows (safe default).</item>
    /// <item>Normal operation: only the current tenant's rows are visible.</item>
    /// </list>
    /// EF Core evaluates <see cref="TenantFilterEnabled"/> and <see cref="CurrentTenantId"/> against the
    /// current running DbContext instance per query — not the model-building instance.
    /// </remarks>
    private void ApplyQueryFilterForEntity<T>(ModelBuilder modelBuilder) where T : class, ITenantEntity
    {
        modelBuilder.Entity<T>()
            .Property(e => e.TenantId)
            .HasConversion(id => id.Value, value => new TenantId(value));

        modelBuilder.Entity<T>()
            .HasQueryFilter(e => !TenantFilterEnabled || e.TenantId == CurrentTenantId);
    }
}
