namespace MicroKit.Tenancy.AspNetCore;

using Microsoft.AspNetCore.Http;

/// <summary>
/// Resolves the current tenant from an HTTP request header.
/// The header name is configurable via <see cref="AspNetCoreMultitenancyOptions.HeaderName"/>
/// (default: <c>X-Tenant-Id</c>). The header value must be a parseable <see cref="Guid"/>.
/// </summary>
public sealed class HeaderTenantResolutionStrategy(
    IHttpContextAccessor httpContextAccessor,
    IOptions<AspNetCoreMultitenancyOptions> options) : ITenantResolutionStrategy
{
    /// <inheritdoc/>
    public int Order => 10;

    /// <inheritdoc/>
    public ValueTask<Result<TenantId>> TryResolveAsync(CancellationToken ct = default)
    {
        var header = httpContextAccessor.HttpContext
            ?.Request.Headers[options.Value.HeaderName].FirstOrDefault();

        if (string.IsNullOrEmpty(header))
            return ValueTask.FromResult(Failure<TenantId>(MultitenancyErrors.TenantNotFound));

        if (!Guid.TryParse(header, out var id))
            return ValueTask.FromResult(Failure<TenantId>(MultitenancyErrors.InvalidTenantId));

        return ValueTask.FromResult(Success(new TenantId(id)));
    }
}
