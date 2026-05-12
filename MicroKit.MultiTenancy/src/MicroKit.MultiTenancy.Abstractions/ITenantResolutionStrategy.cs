namespace MicroKit.MultiTenancy.Abstractions;

/// <summary>
/// Resolves the current tenant identifier from the ambient request context.
/// Implementations are responsible for sourcing the identifier from whatever
/// signal is appropriate (HTTP header, JWT claim, subdomain, etc.).
/// </summary>
public interface ITenantResolutionStrategy
{
    /// <summary>Resolves the tenant identifier, or <see langword="null"/> if unresolvable.</summary>
    ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default);
}
