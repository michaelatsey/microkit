namespace MicroKit.Multitenancy;

/// <summary>
/// A single tenant resolution strategy. Strategies compose into a pipeline
/// via <see cref="ITenantResolver"/>. Never throws — all failures are
/// represented as <see cref="Result{TenantId}"/> failures.
/// </summary>
public interface ITenantResolutionStrategy
{
    /// <summary>
    /// Execution priority. Lower value runs first.
    /// Strategies are tried in ascending <see cref="Order"/>.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Attempts to resolve a tenant identifier from the current execution context.
    /// Returns a <see cref="Result{TenantId}"/> failure if this strategy cannot resolve.
    /// Never throws.
    /// </summary>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// A successful <see cref="Result{TenantId}"/> on resolution,
    /// or a failure result when this strategy cannot identify a tenant.
    /// </returns>
    ValueTask<Result<TenantId>> TryResolveAsync(CancellationToken ct = default);
}
