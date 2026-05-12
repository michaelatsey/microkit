using MicroKit.MultiTenancy.Abstractions;

namespace MicroKit.MultiTenancy.RegionResolvers;

/// <summary>Fallback region resolver that always returns the default region (<c>EU</c>).</summary>
public class DefaultTenantRegionResolver : ITenantRegionResolver
{
    /// <inheritdoc/>
    public ValueTask<string> ResolveAsync(
        string tenantIdentifier,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult("EU");
    }
}