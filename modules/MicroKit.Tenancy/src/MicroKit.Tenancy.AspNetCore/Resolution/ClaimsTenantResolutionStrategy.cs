namespace MicroKit.Tenancy.AspNetCore;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Resolves the current tenant from a JWT or cookie claim.
/// The claim type is configurable via <see cref="AspNetCoreMultitenancyOptions.ClaimType"/>
/// (default: <c>tenant_id</c>). The claim value must be a parseable <see cref="Guid"/>.
/// </summary>
public sealed class ClaimsTenantResolutionStrategy(
    IHttpContextAccessor httpContextAccessor,
    IOptions<AspNetCoreMultitenancyOptions> options) : ITenantResolutionStrategy
{
    /// <inheritdoc/>
    public int Order => 40;

    /// <inheritdoc/>
    public ValueTask<Result<TenantId>> TryResolveAsync(CancellationToken ct = default)
    {
        var value = httpContextAccessor.HttpContext
            ?.User.FindFirst(options.Value.ClaimType)?.Value;

        if (string.IsNullOrEmpty(value))
            return ValueTask.FromResult(Failure<TenantId>(MultitenancyErrors.TenantNotFound));

        if (!Guid.TryParse(value, out var id))
            return ValueTask.FromResult(Failure<TenantId>(MultitenancyErrors.InvalidTenantId));

        return ValueTask.FromResult(Success(new TenantId(id)));
    }
}
