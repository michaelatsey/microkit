namespace MicroKit.Multitenancy.AspNetCore;

using Microsoft.AspNetCore.Http;

/// <summary>
/// Resolves the current tenant by matching the full request host to a <see cref="TenantId"/>
/// via <see cref="AspNetCoreMultitenancyOptions.HostMappings"/> (case-insensitive).
/// </summary>
/// <remarks>
/// Registered only when <see cref="AspNetCoreMultitenancyOptions.EnableHost"/> is <see langword="true"/>.
/// Populate <see cref="AspNetCoreMultitenancyOptions.HostMappings"/> with hostname → <see cref="TenantId"/> pairs.
/// Example: <c>"tenant1.example.com" → new TenantId(guid)</c>.
/// </remarks>
public sealed class HostTenantResolutionStrategy(
    IHttpContextAccessor httpContextAccessor,
    IOptions<AspNetCoreMultitenancyOptions> options) : ITenantResolutionStrategy
{
    /// <inheritdoc/>
    public int Order => 50;

    /// <inheritdoc/>
    public ValueTask<Result<TenantId>> TryResolveAsync(CancellationToken ct = default)
    {
        var host = httpContextAccessor.HttpContext?.Request.Host.Host;

        if (string.IsNullOrEmpty(host))
            return ValueTask.FromResult(Failure<TenantId>(MultitenancyErrors.TenantNotFound));

        if (!options.Value.HostMappings.TryGetValue(host, out var tenantId))
            return ValueTask.FromResult(Failure<TenantId>(MultitenancyErrors.TenantNotFound));

        return ValueTask.FromResult(Success(tenantId));
    }
}
