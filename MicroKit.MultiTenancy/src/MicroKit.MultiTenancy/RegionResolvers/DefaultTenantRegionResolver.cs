using MicroKit.MultiTenancy.Abstractions;

namespace MicroKit.MultiTenancy.RegionResolvers;

public class DefaultTenantRegionResolver : ITenantRegionResolver
{
    public ValueTask<string> ResolveAsync(
        string tenantIdentifier,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult("EU");
    }
}