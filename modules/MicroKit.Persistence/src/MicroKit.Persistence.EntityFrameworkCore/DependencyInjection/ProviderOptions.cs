namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Opt-in resilience and pooling for a provider registration (<c>UsePostgreSQL</c>,
/// <c>UseSqlServer</c>). Everything is off by default.
/// </summary>
public sealed record ProviderOptions
{
    private readonly int? _poolSize;

    /// <summary>
    /// Gets the transient-failure retry policy. <see langword="null"/> (the default) keeps the
    /// provider's non-retrying execution strategy.
    /// </summary>
    /// <remarks>
    /// With retries on, the provider's strategy rejects a transaction a caller opens itself with
    /// <c>Database.BeginTransaction</c> outside the strategy: the next query or save on that context
    /// throws <see cref="InvalidOperationException"/>. Such code must open its transaction inside
    /// <c>Database.CreateExecutionStrategy().ExecuteAsync</c>
    /// (https://learn.microsoft.com/ef/core/miscellaneous/connection-resiliency#execution-strategies-and-transactions).
    /// <see cref="ITransactionalContext.ExecuteAsync{TState}"/> runs its transaction inside the
    /// strategy and retries it as a whole. A retried operation runs again from the start: it must be
    /// idempotent.
    /// </remarks>
    public RetryOnFailureOptions? Retry { get; init; }

    /// <summary>
    /// Gets the maximum number of context instances kept by EF Core's context pool.
    /// <see langword="null"/> (the default) registers the context without pooling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pooling resets and reuses context instances (<c>AddDbContextPool</c>). A pooled context's
    /// constructor-injected scoped services are resolved once from the root provider and shared by
    /// every lease — EF Core's <c>AddDbContextPool</c> remarks: "scoped services injected into the
    /// context will only be resolved once from the initial scope"
    /// (https://learn.microsoft.com/dotnet/api/microsoft.extensions.dependencyinjection.entityframeworkservicecollectionextensions.adddbcontextpool?view=efcore-10.0).
    /// EF Core does not reject such a context, so a tenant-aware context such as
    /// <c>MicroKit.Tenancy.EntityFrameworkCore.MultitenantDbContext</c>, whose constructor also takes
    /// an <c>ITenantContextAccessor</c>, must not be pooled.
    /// </para>
    /// <para>
    /// <c>UsePostgreSQL</c> and <c>UseSqlServer</c> therefore throw <see cref="ArgumentException"/> at
    /// registration when this is set and the context's only public constructor does not take just
    /// <c>DbContextOptions</c> or <c>DbContextOptions&lt;TContext&gt;</c>.
    /// </para>
    /// <para>
    /// Pooling concerns the context only. The unit of work registered by <c>AddUnitOfWork</c> stays
    /// scoped and keeps no state between scopes.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int? PoolSize
    {
        get => _poolSize;
        init
        {
            if (value is < 1)
                throw new ArgumentOutOfRangeException(nameof(PoolSize), value, "A context pool holds at least one instance.");
            _poolSize = value;
        }
    }
}

/// <summary>
/// The provider's retrying execution strategy: how many times, and with what maximum back-off
/// delay, a transient failure is retried.
/// </summary>
public sealed record RetryOnFailureOptions
{
    /// <summary>
    /// Initializes a new <see cref="RetryOnFailureOptions"/>. The defaults are EF Core's own.
    /// </summary>
    /// <param name="maxRetryCount">The maximum number of retries. Must be at least 1. Defaults to 6.</param>
    /// <param name="maxRetryDelay">
    /// The maximum delay between two retries. Must be positive. Defaults to 30 seconds.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    public RetryOnFailureOptions(int maxRetryCount = 6, TimeSpan? maxRetryDelay = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetryCount, 1);
        var delay = maxRetryDelay ?? TimeSpan.FromSeconds(30);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(delay, TimeSpan.Zero, nameof(maxRetryDelay));

        MaxRetryCount = maxRetryCount;
        MaxRetryDelay = delay;
    }

    /// <summary>Gets the maximum number of retries.</summary>
    public int MaxRetryCount { get; }

    /// <summary>Gets the maximum delay between two retries.</summary>
    public TimeSpan MaxRetryDelay { get; }
}
