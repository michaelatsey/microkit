using Microsoft.AspNetCore.Http;

namespace MicroKit.MultiTenancy.ResolutionStrategies;

/// <summary>
/// Specialisation of <see cref="Abstractions.ITenantResolutionStrategy"/> for strategies
/// that source the tenant identifier from an HTTP request.
/// </summary>
public interface IHttpTenantResolutionStrategy : Abstractions.ITenantResolutionStrategy
{
    /// <summary>Resolves the tenant identifier from the given <paramref name="context"/>.</summary>
    ValueTask<string?> GetTenantIdentifierAsync(HttpContext context);
}
