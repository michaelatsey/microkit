using MicroKit.MultiTenancy.Abstractions;
using Microsoft.AspNetCore.Http;

namespace MicroKit.MultiTenancy.ResolutionStrategies;

public class HeaderResolutionStrategy : ITenantResolutionStrategy
{
    private readonly string _headerName;

    public HeaderResolutionStrategy(string headerName)
    {
        _headerName = headerName;
    }

    public async Task<string?> GetTenantIdentifierAsync(HttpContext context)
    {
        if (! await Task.FromResult(context.Request.Headers
            .TryGetValue(_headerName, out var tenantHeader)))
        {
            return null;
        }
        return tenantHeader.FirstOrDefault();
    }
}
