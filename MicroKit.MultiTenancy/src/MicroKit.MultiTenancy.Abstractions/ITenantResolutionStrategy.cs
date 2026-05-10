using Microsoft.AspNetCore.Http;

namespace MicroKit.MultiTenancy.Abstractions;

public interface ITenantResolutionStrategy
{
    Task<string?> GetTenantIdentifierAsync(HttpContext context);
}
