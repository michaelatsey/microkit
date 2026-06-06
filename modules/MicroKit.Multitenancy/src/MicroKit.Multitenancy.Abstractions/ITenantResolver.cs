namespace MicroKit.Multitenancy;

/// <summary>
/// Orchestrates registered <see cref="ITenantResolutionStrategy"/> instances
/// to resolve the current tenant. Short-circuits on first success. Never throws.
/// </summary>
public interface ITenantResolver
{
    /// <summary>
    /// Resolves the current tenant by iterating registered strategies in
    /// <see cref="ITenantResolutionStrategy.Order"/> ascending.
    /// Short-circuits on the first successful resolution.
    /// </summary>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// A successful <see cref="Result{ITenantInfo}"/> when a strategy resolved the tenant,
    /// or a failure result if no strategy could identify a tenant.
    /// </returns>
    ValueTask<Result<ITenantInfo>> ResolveAsync(CancellationToken ct = default);
}
