namespace MicroKit.Tenancy.AspNetCore;

using Microsoft.AspNetCore.Http;

/// <summary>
/// Resolves the current tenant from a route parameter.
/// The parameter name is configurable via <see cref="AspNetCoreMultitenancyOptions.RouteParameterName"/>
/// (default: <c>tenantId</c>). The route value must be a parseable <see cref="Guid"/>.
/// </summary>
public sealed class RouteDataTenantResolutionStrategy(
    IHttpContextAccessor httpContextAccessor,
    IOptions<AspNetCoreMultitenancyOptions> options) : ITenantResolutionStrategy
{
    /// <inheritdoc/>
    public int Order => 20;

    /// <inheritdoc/>
    public ValueTask<Result<TenantId>> TryResolveAsync(CancellationToken ct = default)
    {
        var value = httpContextAccessor.HttpContext
            ?.Request.RouteValues[options.Value.RouteParameterName]?.ToString();

        if (string.IsNullOrEmpty(value))
            return ValueTask.FromResult(Failure<TenantId>(MultitenancyErrors.TenantNotFound));

        if (!Guid.TryParse(value, out var id))
            return ValueTask.FromResult(Failure<TenantId>(MultitenancyErrors.InvalidTenantId));

        return ValueTask.FromResult(Success(new TenantId(id)));
    }
}
